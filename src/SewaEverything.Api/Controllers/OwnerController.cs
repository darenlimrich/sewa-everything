using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Payments;
using SewaEverything.Infrastructure.Security;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("owner")]
[Authorize(Roles = Roles.Owner)]
public sealed class OwnerController(
    SewaDbContext db,
    IPasswordHasher hasher,
    TimeProvider clock,
    IMidtransCredentials credentials,
    IPaymentGateway gateway,
    ISecretProtector protector,
    TotpService totp) : ControllerBase
{
    [HttpGet("settings")]
    [ProducesResponseType<PlatformSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlatformSettingsResponse>> GetSettings(CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        return Ok(settings.ToResponse());
    }

    [HttpPut("settings")]
    [ProducesResponseType<PlatformSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlatformSettingsResponse>> UpdateSettings(
        UpdatePlatformSettingsRequest request, CancellationToken ct)
    {
        if (request.CommissionMode is not (CommissionModes.Deduct or CommissionModes.OnTop))
        {
            ModelState.AddModelError(nameof(request.CommissionMode),
                $"Mode komisi hanya boleh '{CommissionModes.Deduct}' atau '{CommissionModes.OnTop}'.");
            return ValidationProblem(ModelState);
        }

        var settings = await LoadSettingsAsync(ct);

        settings.CommissionRate   = request.CommissionRate;
        settings.CommissionMode   = CommissionModes.FromDbValue(request.CommissionMode);
        settings.ApprovalMinutes  = request.ApprovalMinutes;
        settings.PaymentMinutes   = request.PaymentMinutes;
        settings.ReturnWindowDays = request.ReturnWindowDays;
        settings.UpdatedBy        = HttpContext.User.UserId();

        await db.SaveChangesAsync(ct);

        return Ok(settings.ToResponse());
    }

    [HttpGet("payment-gateway")]
    [ProducesResponseType<PaymentGatewayResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PaymentGatewayResponse>> PaymentGateway(CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);
        var effective = await credentials.CurrentAsync(ct);

        var fromDatabase = !string.IsNullOrWhiteSpace(settings.MidtransServerKey);
        var configured   = !string.IsNullOrWhiteSpace(effective.ServerKey);

        return Ok(new PaymentGatewayResponse
        {
            Provider      = MidtransPaymentGateway.ProviderName,
            IsConfigured  = configured,
            Source        = fromDatabase ? GatewayCredentialSources.Database
                          : configured   ? GatewayCredentialSources.Configuration
                                         : GatewayCredentialSources.None,
            IsProduction  = effective.IsProduction,
            ClientKey     = string.IsNullOrWhiteSpace(effective.ClientKey) ? null : effective.ClientKey,
            ServerKeyHint = configured ? MaskTail(effective.ServerKey) : null,
            UpdatedAt     = settings.UpdatedAt
        });
    }

    [HttpPut("payment-gateway")]
    [ProducesResponseType<PaymentGatewayResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PaymentGatewayResponse>> UpdatePaymentGateway(
        UpdatePaymentGatewayRequest request, CancellationToken ct)
    {
        var settings = await LoadSettingsAsync(ct);

        if (!protector.IsConfigured)
        {
            return Problem(
                title: "Penyimpanan rahasia belum siap",
                detail: "Security:SecretKey belum diatur di server, jadi server key tidak dapat " +
                        "disimpan dalam keadaan terenkripsi. Hubungi operator server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var serverKey = string.IsNullOrWhiteSpace(request.ServerKey)
            ? protector.Reveal(settings.MidtransServerKey)
            : request.ServerKey.Trim();

        var clientKey = request.ClientKey.Trim();

        if (string.IsNullOrWhiteSpace(serverKey))
        {
            ModelState.AddModelError(nameof(request.ServerKey),
                "Server key wajib diisi saat pertama kali menyimpan.");
            return ValidationProblem(ModelState);
        }

        switch (await gateway.VerifyServerKeyAsync(serverKey, request.IsProduction, ct))
        {
            case GatewayKeyVerdict.WrongEnvironment:
                ModelState.AddModelError(nameof(request.ServerKey), request.IsProduction
                    ? "Midtrans produksi tidak mengenali server key ini. Pastikan kuncinya " +
                      "disalin dari lingkungan Produksi, bukan Sandbox."
                    : "Midtrans sandbox tidak mengenali server key ini. Pastikan kuncinya " +
                      "disalin dari lingkungan Sandbox, bukan Produksi.");
                break;

            case GatewayKeyVerdict.Unverifiable:
                ModelState.AddModelError(nameof(request.ServerKey),
                    "Midtrans tidak dapat dihubungi untuk memastikan kunci ini milik lingkungan " +
                    "yang dipilih. Kunci tidak disimpan — coba lagi.");
                break;
        }

        if (request.IsProduction && IsSandboxKey(clientKey))
        {
            ModelState.AddModelError(nameof(request.ClientKey),
                "Mode produksi dipilih, tapi client key-nya kunci sandbox (berawalan 'SB-').");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        settings.MidtransServerKey    = protector.Protect(serverKey);
        settings.MidtransClientKey    = clientKey;
        settings.MidtransIsProduction = request.IsProduction;
        settings.UpdatedBy            = HttpContext.User.UserId();

        await db.SaveChangesAsync(ct);

        return Ok(new PaymentGatewayResponse
        {
            Provider      = MidtransPaymentGateway.ProviderName,
            IsConfigured  = true,
            Source        = GatewayCredentialSources.Database,
            IsProduction  = settings.MidtransIsProduction,
            ClientKey     = settings.MidtransClientKey,
            ServerKeyHint = MaskTail(serverKey),
            UpdatedAt     = settings.UpdatedAt
        });
    }

    private static bool IsSandboxKey(string key) =>
        key.StartsWith("SB-", StringComparison.OrdinalIgnoreCase);

    private static string MaskTail(string secret) =>
        secret.Length <= 4 ? "••••" : $"••••{secret[^4..]}";

    [HttpGet("revenue")]
    [ProducesResponseType<RevenueSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RevenueSummaryResponse>> Revenue(CancellationToken ct)
    {
        var now   = clock.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-30);

        var money = await db.Payments.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                MoneyIn = g.Sum(p =>
                    p.Direction == PaymentDirection.In && p.Status == PaymentStatus.Paid
                        ? p.Amount : 0m),

                MoneyOut = g.Sum(p =>
                    p.Direction == PaymentDirection.Out && p.Status == PaymentStatus.Paid
                        ? p.Amount : 0m),

                PayoutsDue = g.Sum(p =>
                    p.Direction == PaymentDirection.Out && p.Status == PaymentStatus.Pending
                        ? p.Amount : 0m),

                PendingPayoutCount = g.Sum(p =>
                    p.Direction == PaymentDirection.Out && p.Status == PaymentStatus.Pending
                        ? 1 : 0),

                Commission = g.Sum(p =>
                    p.Kind == PaymentKind.PlatformFee && p.Status == PaymentStatus.Paid
                        ? p.Amount : 0m),

                CommissionLast30Days = g.Sum(p =>
                    p.Kind == PaymentKind.PlatformFee && p.Status == PaymentStatus.Paid
                    && p.CreatedAt >= since
                        ? p.Amount : 0m)
            })
            .FirstOrDefaultAsync(ct);

        var moneyIn    = money?.MoneyIn ?? 0m;
        var moneyOut   = money?.MoneyOut ?? 0m;
        var payoutsDue = money?.PayoutsDue ?? 0m;
        var commission = money?.Commission ?? 0m;
        var cashHeld   = moneyIn - moneyOut;

        var completed = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Completed, ct);
        var active    = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Active, ct);
        var disputes  = await db.Disputes.CountAsync(d => d.Status == DisputeStatus.Open, ct);

        return Ok(new RevenueSummaryResponse
        {
            MoneyIn              = moneyIn,
            MoneyOut             = moneyOut,
            CashHeld             = cashHeld,
            PayoutsDue           = payoutsDue,
            PendingPayoutCount   = money?.PendingPayoutCount ?? 0,
            CommissionEarned     = commission,
            CommissionLast30Days = money?.CommissionLast30Days ?? 0m,

            InEscrow             = cashHeld - payoutsDue - commission,

            CompletedBookings    = completed,
            ActiveBookings       = active,
            OpenDisputes         = disputes,
            GeneratedAt          = now
        });
    }

    [HttpGet("transactions")]
    [ProducesResponseType<PagedResponse<OwnerTransactionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<OwnerTransactionResponse>>> Transactions(
        [FromQuery] OwnerTransactionQuery query, CancellationToken ct)
    {
        var kind = PaymentKinds.TryFromDbValue(query.Kind);
        var status = PaymentStatuses.TryFromDbValue(query.Status);

        if (query.Kind is not null && kind is null)
        {
            ModelState.AddModelError(nameof(query.Kind), $"Jenis '{query.Kind}' tidak dikenal.");
        }

        if (query.Status is not null && status is null)
        {
            ModelState.AddModelError(nameof(query.Status), $"Status '{query.Status}' tidak dikenal.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var rows = db.Payments.AsNoTracking();

        if (kind is not null)
        {
            rows = rows.Where(p => p.Kind == kind);
        }

        if (status is not null)
        {
            rows = rows.Where(p => p.Status == status);
        }

        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new OwnerTransactionResponse
            {
                Id        = p.Id,
                Reference = p.Reference,
                BookingId = p.BookingId,

                BookingReference = db.Bookings.Where(b => b.Id == p.BookingId)
                    .Select(b => b.Reference).FirstOrDefault() ?? string.Empty,

                ItemTitle = db.Bookings.Where(b => b.Id == p.BookingId)
                    .Select(b => b.Item!.Title).FirstOrDefault() ?? string.Empty,

                Kind      = p.Kind.ToDbValue(),
                Direction = p.Direction.ToDbValue(),
                Amount    = p.Amount,
                Status    = p.Status.ToDbValue(),
                Method    = p.Method.ToDbValue(),
                Channel   = p.Channel,

                CounterpartyName = db.Users.Where(u => u.Id == p.CounterpartyId)
                    .Select(u => u.Name).FirstOrDefault(),

                SettledAt = p.SettledAt,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponse<OwnerTransactionResponse>
        {
            Items    = items,
            Page     = query.Page,
            PageSize = query.PageSize,
            Total    = total
        });
    }

    [HttpGet("admins")]
    [ProducesResponseType<IReadOnlyList<AdminAccountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdminAccountResponse>>> Admins(CancellationToken ct)
    {
        var admins = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        var berdua = await db.UserTotps.AsNoTracking()
            .Where(t => t.ConfirmedAt != null)
            .Select(t => t.UserId)
            .ToListAsync(ct);

        return Ok(admins
            .Select(a => a.ToAdminResponse() with { TwoFactorEnabled = berdua.Contains(a.Id) })
            .ToList());
    }

    [HttpPost("admins")]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminAccountResponse>> CreateAdmin(
        CreateAdminRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existing = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        if (existing is not null)
        {
            return Problem(
                title: "Email sudah terdaftar",
                detail: existing is { Role: UserRole.Admin, DeactivatedAt: not null }
                    ? "Akun admin dengan email ini sudah ada, hanya sedang nonaktif. Pulihkan " +
                      "aksesnya alih-alih membuat akun baru."
                    : "Email ini sudah dipakai akun lain. Gunakan email berbeda.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var admin = new User
        {
            Role         = UserRole.Admin,
            Name         = request.Name.Trim(),
            Email        = email,
            Phone        = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = hasher.Hash(request.Password)
        };

        db.Users.Add(admin);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();

            return Problem(
                title: "Email sudah terdaftar",
                detail: "Email ini sudah dipakai akun lain. Gunakan email berbeda.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return Created($"/owner/admins/{admin.Id}", admin.ToAdminResponse());
    }

    [HttpPost("admins/{id:guid}/access")]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminAccountResponse>> SetAdminAccess(
        Guid id, SetAdminAccessRequest request, CancellationToken ct)
    {
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (admin is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: $"Tidak ada akun dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (admin.Role != UserRole.Admin)
        {
            return Problem(
                title: "Akun ini bukan admin",
                detail: $"Panel ini hanya mengelola akun admin. Akun ini ber-role " +
                        $"'{admin.Role.ToDbValue()}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Active)
        {
            admin.Reactivate();
        }
        else
        {
            admin.Deactivate(clock.GetUtcNow().UtcDateTime);
        }

        await db.SaveChangesAsync(ct);

        return Ok(admin.ToAdminResponse());
    }

    [HttpPost("admins/{id:guid}/2fa/reset")]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminAccountResponse>> ResetAdminTwoFactor(
        Guid id, CancellationToken ct)
    {
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        if (admin is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: $"Tidak ada akun dengan id {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (admin.Role != UserRole.Admin)
        {
            return Problem(
                title: "Akun ini bukan admin",
                detail: $"Panel ini hanya mengelola akun admin. Akun ini ber-role " +
                        $"'{admin.Role.ToDbValue()}'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await totp.DisableAsync(admin.Id, ct))
        {
            return Problem(
                title: "Verifikasi dua langkah belum aktif",
                detail: "Akun ini tidak memakai aplikasi autentikator, jadi tidak ada yang " +
                        "perlu dilepas.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(admin.ToAdminResponse() with { TwoFactorEnabled = false });
    }

    private async Task<PlatformSettings> LoadSettingsAsync(CancellationToken ct) =>
        await db.PlatformSettings.FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException(
            "Baris platform_settings tidak ada. Terapkan db/migrations/0001_init.sql dulu.");
}
