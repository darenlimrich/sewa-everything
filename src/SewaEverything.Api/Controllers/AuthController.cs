using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SewaEverything.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Persistence;
using SewaEverything.Infrastructure.Storage;

namespace SewaEverything.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(
    SewaDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    RefreshTokenService refreshTokens,
    PasswordResetService passwordResets,
    TotpService totp,
    TimeProvider clock,
    IPhotoStorage photos,
    IOptions<PhotoStorageOptions> photoOptions,
    IOptions<LoginLockoutOptions> lockoutOptions) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(SewaRateLimits.Register)]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (request.Role is not (Roles.Seller or Roles.Renter))
        {
            ModelState.AddModelError(nameof(request.Role),
                $"Role hanya boleh '{Roles.Seller}' atau '{Roles.Renter}'.");
            return ValidationProblem(ModelState);
        }

        var email = Normalize(request.Email);

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email, ct))
        {
            return Problem(
                title: "Email sudah terdaftar",
                detail: "Email ini sudah dipakai akun lain. Coba masuk, atau gunakan email berbeda.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var user = new User
        {
            Role         = Roles.FromDbValue(request.Role),
            Name         = request.Name.Trim(),
            Email        = email,
            Phone        = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PasswordHash = hasher.Hash(request.Password)
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return Problem(
                title: "Email sudah terdaftar",
                detail: "Email ini sudah dipakai akun lain. Coba masuk, atau gunakan email berbeda.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return Created($"/users/{user.Id}", await SessionForAsync(user, ct));
    }

    [HttpPost("login")]
    [EnableRateLimiting(SewaRateLimits.Login)]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        AuthenticateAsync(request, staffDoor: false, ct);

    [HttpPost("staff/login")]
    [EnableRateLimiting(SewaRateLimits.Login)]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<AuthResponse>> StaffLogin(LoginRequest request, CancellationToken ct) =>
        AuthenticateAsync(request, staffDoor: true, ct);

    private async Task<ActionResult<AuthResponse>> AuthenticateAsync(
        LoginRequest request, bool staffDoor, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        if (user is null)
        {
            hasher.Hash(request.Password);
            return InvalidCredentials();
        }

        var lockout = lockoutOptions.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        if (lockout.Enabled && user.IsLockedAt(now))
        {
            return AccountLocked(user.LockedUntil!.Value, now);
        }

        var check = hasher.Verify(user.PasswordHash, request.Password);

        if (check == PasswordCheck.Failed)
        {
            if (!lockout.Enabled)
            {
                return InvalidCredentials();
            }

            user.RegisterFailedLogin(now, lockout.Window, lockout.MaxAttempts, lockout.Lock);
            await db.SaveChangesAsync(ct);

            return user.IsLockedAt(now)
                ? AccountLocked(user.LockedUntil!.Value, now)
                : InvalidCredentials();
        }

        if (!MayUseDoor(user.Role, staffDoor))
        {
            return InvalidCredentials();
        }

        if (!user.IsActive)
        {
            return Problem(
                title: "Akses akun dicabut",
                detail: "Akun ini sudah dinonaktifkan. Hubungi owner platform kalau menurut " +
                        "Anda ini keliru.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var kedua = await totp.VerifyForLoginAsync(user.Id, request.TotpCode, ct);

        if (kedua == TotpLoginCheck.Missing)
        {
            return TotpRequired(
                "Kode autentikasi diperlukan",
                "Akun ini memakai verifikasi dua langkah. Masukkan kode 6 angka dari aplikasi " +
                "autentikator Anda, atau salah satu kode pemulihan.");
        }

        if (kedua == TotpLoginCheck.Wrong)
        {
            if (!lockout.Enabled)
            {
                return WrongTotpCode();
            }

            user.RegisterFailedLogin(now, lockout.Window, lockout.MaxAttempts, lockout.Lock);
            await db.SaveChangesAsync(ct);

            return user.IsLockedAt(now)
                ? AccountLocked(user.LockedUntil!.Value, now)
                : WrongTotpCode();
        }

        if (check == PasswordCheck.OkNeedsRehash)
        {
            user.PasswordHash = hasher.Hash(request.Password);
        }

        if (user.FailedLoginCount > 0 || user.LockedUntil is not null)
        {
            user.RegisterSuccessfulLogin();
        }

        await db.SaveChangesAsync(ct);

        return Ok(await SessionForAsync(user, ct));
    }

    private static bool MayUseDoor(UserRole role, bool staffDoor) => role.IsStaff() == staffDoor;

    [HttpPost("forgot-password")]
    [EnableRateLimiting(SewaRateLimits.ForgotPassword)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await passwordResets.RequestAsync(request.Email, ct);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(SewaRateLimits.ResetPassword)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await passwordResets.ResetAsync(
            request.Token, request.Password, PasswordPolicy.Periksa, ct);

        switch (result.Outcome)
        {
            case PasswordResetOutcome.Reset:
                return NoContent();

            case PasswordResetOutcome.WeakPassword:
                ModelState.AddModelError(nameof(request.Password), result.Message!);
                return ValidationProblem(ModelState);

            case PasswordResetOutcome.AccountDeactivated:
                return Problem(
                    title: "Akses akun dicabut",
                    detail: "Akun ini sudah dinonaktifkan. Hubungi owner platform kalau menurut " +
                            "Anda ini keliru.",
                    statusCode: StatusCodes.Status403Forbidden);

            default:
                return Problem(
                    title: "Tautan tidak berlaku",
                    detail: "Tautan atur ulang ini sudah kedaluwarsa atau sudah pernah dipakai. " +
                            "Minta tautan baru lewat halaman lupa kata sandi.",
                    statusCode: StatusCodes.Status410Gone);
        }
    }

    [HttpPost("refresh")]
    [EnableRateLimiting(SewaRateLimits.Refresh)]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct)
    {
        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, ct);

        switch (rotation.Outcome)
        {
            case RefreshOutcome.Rotated:
                var access = tokens.Create(rotation.User!);

                return Ok(new AuthResponse
                {
                    AccessToken           = access.Value,
                    ExpiresAt             = access.ExpiresAt,
                    RefreshToken          = rotation.Token!.Value,
                    RefreshTokenExpiresAt = rotation.Token.ExpiresAt,
                    User                  = rotation.User!.ToResponse()
                });

            case RefreshOutcome.Reused:
                return Problem(
                    title: "Sesi diakhiri",
                    detail: "Token perpanjangan ini sudah pernah dipakai. Demi keamanan, seluruh " +
                            "sesi tersebut diakhiri. Silakan masuk kembali.",
                    statusCode: StatusCodes.Status401Unauthorized);

            case RefreshOutcome.AccountDeactivated:
                return Problem(
                    title: "Akses akun dicabut",
                    detail: "Akun ini sudah dinonaktifkan. Hubungi owner platform kalau menurut " +
                            "Anda ini keliru.",
                    statusCode: StatusCodes.Status403Forbidden);

            default:
                return Problem(
                    title: "Sesi sudah tidak berlaku",
                    detail: "Token perpanjangan tidak dikenal atau sudah kedaluwarsa. Silakan masuk kembali.",
                    statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        await refreshTokens.RevokeAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null
            ? Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized)
            : Ok(user.ToResponse());
    }

    [HttpPut("me")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> UpdateMe(UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var name = request.Name.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        if (name.Length < 2)
        {
            return Problem(
                title: "Nama tidak valid",
                detail: "Nama harus 2–120 karakter.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        user.Name = name;
        user.Phone = phone;
        await db.SaveChangesAsync(ct);

        return Ok(user.ToResponse());
    }

    [HttpPost("me/photo")]
    [Authorize]
    [RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> UploadPhoto(
        [FromForm] IFormFile? file, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var batas = photoOptions.Value.MaxBytes;

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "Berkas foto wajib disertakan.");
            return ValidationProblem(ModelState);
        }

        if (file.Length > batas)
        {
            ModelState.AddModelError(nameof(file),
                $"Ukuran foto maksimal {batas / (1024 * 1024)} MB.");
            return ValidationProblem(ModelState);
        }

        await using var content = file.OpenReadStream();

        var header = new byte[ImageSniffer.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

        if (!ImageSniffer.TryDetect(header.AsSpan(0, read), out var format))
        {
            ModelState.AddModelError(nameof(file), "Berkas ini bukan gambar JPEG, PNG, atau WebP.");
            return ValidationProblem(ModelState);
        }

        content.Position = 0;

        using var mentah = new MemoryStream();
        await content.CopyToAsync(mentah, ct);

        var bersih = ImageSanitizer.Sanitize(mentah.GetBuffer().AsSpan(0, (int)mentah.Length), format);

        if (!bersih.Ok)
        {
            ModelState.AddModelError(nameof(file), bersih.Error!);
            return ValidationProblem(ModelState);
        }

        using var siap = new MemoryStream(bersih.Bytes!, writable: false);

        var lama = user.AvatarUrl;
        var baru = await photos.SaveAsync(siap, format, ct);

        user.AvatarUrl = baru;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await photos.DeleteAsync(baru, CancellationToken.None);
            throw;
        }

        if (lama is not null)
        {
            await photos.DeleteAsync(lama, CancellationToken.None);
        }

        return Ok(user.ToResponse());
    }

    [HttpDelete("me/photo")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> DeletePhoto(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var lama = user.AvatarUrl;

        if (lama is null)
        {
            return Ok(user.ToResponse());
        }

        user.AvatarUrl = null;
        await db.SaveChangesAsync(ct);
        await photos.DeleteAsync(lama, CancellationToken.None);

        return Ok(user.ToResponse());
    }

    [HttpPost("change-password")]
    [EnableRateLimiting(SewaRateLimits.ResetPassword)]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Problem(
                title: "Akun tidak ditemukan",
                detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (hasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
        {
            ModelState.AddModelError(nameof(request.CurrentPassword), "Kata sandi saat ini salah.");
            return ValidationProblem(ModelState);
        }

        if (PasswordPolicy.Periksa(request.NewPassword, user.Email, user.Name) is { } keluhan)
        {
            ModelState.AddModelError(nameof(request.NewPassword), keluhan);
            return ValidationProblem(ModelState);
        }

        if (hasher.Verify(user.PasswordHash, request.NewPassword) != PasswordCheck.Failed)
        {
            ModelState.AddModelError(nameof(request.NewPassword),
                "Kata sandi baru harus berbeda dari kata sandi sekarang.");
            return ValidationProblem(ModelState);
        }

        user.PasswordHash = hasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(ct);

        await refreshTokens.RevokeAllForUserAsync(
            user.Id, RefreshTokenRevokeReasons.PasswordReset, ct);

        return NoContent();
    }

    [HttpGet("2fa")]
    [Authorize(Roles = Roles.StaffOrSeller)]
    [ProducesResponseType<TotpStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TotpStatusResponse>> TwoFactorStatus(CancellationToken ct)
    {
        var status = await totp.StatusAsync(HttpContext.User.UserId(), ct);

        return Ok(new TotpStatusResponse
        {
            Enabled           = status.Enabled,
            PendingSetup      = status.PendingSetup,
            EnabledAt         = status.EnabledAt,
            RecoveryCodesLeft = status.RecoveryCodesLeft
        });
    }

    [HttpPost("2fa/setup")]
    [Authorize(Roles = Roles.StaffOrSeller)]
    [ProducesResponseType<TotpSetupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TotpSetupResponse>> TwoFactorSetup(
        TotpSetupRequest request, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);

        if (user is null)
        {
            return SessionGone();
        }

        if (hasher.Verify(user.PasswordHash, request.Password) == PasswordCheck.Failed)
        {
            return WrongPassword();
        }

        var pendaftaran = await totp.BeginAsync(user, ct);

        return pendaftaran.Outcome switch
        {
            TotpEnrollOutcome.AlreadyEnabled => Problem(
                title: "Verifikasi dua langkah sudah aktif",
                detail: "Matikan dulu yang sekarang sebelum mendaftarkan aplikasi autentikator baru.",
                statusCode: StatusCodes.Status409Conflict),

            TotpEnrollOutcome.ProtectionUnavailable => Problem(
                title: "Kunci enkripsi belum diisi",
                detail: "Security:SecretKey belum diatur di server, jadi rahasia autentikator " +
                        "tidak dapat disimpan dengan aman. Hubungi operator platform.",
                statusCode: StatusCodes.Status503ServiceUnavailable),

            _ => Ok(new TotpSetupResponse
            {
                Secret = pendaftaran.Secret!,
                Uri    = pendaftaran.Uri!
            })
        };
    }

    [HttpPost("2fa/enable")]
    [Authorize(Roles = Roles.StaffOrSeller)]
    [ProducesResponseType<TotpEnableResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TotpEnableResponse>> TwoFactorEnable(
        TotpCodeRequest request, CancellationToken ct)
    {
        var hasil = await totp.ConfirmAsync(HttpContext.User.UserId(), request.Code, ct);

        return hasil.Outcome switch
        {
            TotpConfirmOutcome.Enabled => Ok(new TotpEnableResponse
            {
                RecoveryCodes = hasil.RecoveryCodes
            }),

            TotpConfirmOutcome.NotStarted => Problem(
                title: "Pendaftaran belum dimulai",
                detail: "Masukkan dulu kuncinya ke aplikasi autentikator, lalu kirim kode " +
                        "yang ditampilkannya.",
                statusCode: StatusCodes.Status409Conflict),

            TotpConfirmOutcome.AlreadyEnabled => Problem(
                title: "Verifikasi dua langkah sudah aktif",
                detail: "Tidak ada yang perlu dinyalakan lagi.",
                statusCode: StatusCodes.Status409Conflict),

            _ => BadTotpCode()
        };
    }

    [HttpPost("2fa/disable")]
    [Authorize(Roles = Roles.StaffOrSeller)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TwoFactorDisable(
        TotpDisableRequest request, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);

        if (user is null)
        {
            return SessionGone();
        }

        if (hasher.Verify(user.PasswordHash, request.Password) == PasswordCheck.Failed)
        {
            return WrongPassword();
        }

        var kedua = await totp.VerifyForLoginAsync(user.Id, request.Code, ct);

        if (kedua == TotpLoginCheck.NotRequired)
        {
            return Problem(
                title: "Verifikasi dua langkah belum aktif",
                detail: "Tidak ada yang perlu dimatikan.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (kedua != TotpLoginCheck.Ok)
        {
            return BadTotpCode();
        }

        await totp.DisableAsync(user.Id, ct);

        return NoContent();
    }

    private async Task<User?> CurrentUserAsync(CancellationToken ct)
    {
        var userId = HttpContext.User.UserId();

        return await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    private async Task<AuthResponse> SessionForAsync(User user, CancellationToken ct)
    {
        var access  = tokens.Create(user);
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);

        return new AuthResponse
        {
            AccessToken           = access.Value,
            ExpiresAt             = access.ExpiresAt,
            RefreshToken          = refresh.Value,
            RefreshTokenExpiresAt = refresh.ExpiresAt,
            User                  = user.ToResponse()
        };
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private ObjectResult AccountLocked(DateTime until, DateTime now)
    {
        var menit = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));

        Response.Headers.RetryAfter =
            ((int)Math.Ceiling((until - now).TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return Problem(
            title: "Akun dikunci sementara",
            detail: $"Kata sandi salah terlalu sering. Coba lagi dalam {menit} menit, " +
                    "atau tunggu sampai kunciannya habis dengan sendirinya.",
            statusCode: StatusCodes.Status423Locked);
    }

    private ObjectResult InvalidCredentials() => Problem(
        title: "Email atau kata sandi salah",
        detail: "Periksa kembali email dan kata sandi Anda.",
        statusCode: StatusCodes.Status401Unauthorized);

    private ObjectResult TotpRequired(string title, string detail)
    {
        var hasil = Problem(
            title: title, detail: detail, statusCode: StatusCodes.Status401Unauthorized);

        if (hasil.Value is ProblemDetails masalah)
        {
            masalah.Extensions["totpRequired"] = true;
        }

        return hasil;
    }

    private ObjectResult WrongTotpCode() => TotpRequired(
        "Kode autentikasi salah",
        "Kode itu tidak cocok atau sudah pernah dipakai. Kodenya berganti tiap 30 detik — " +
        "coba kode terbaru dari aplikasi autentikator Anda.");

    private ActionResult BadTotpCode()
    {
        ModelState.AddModelError("code",
            "Kode itu tidak cocok atau sudah pernah dipakai. Kodenya berganti tiap 30 detik.");

        return ValidationProblem(ModelState);
    }

    private ObjectResult WrongPassword() => Problem(
        title: "Kata sandi salah",
        detail: "Kata sandi akun Anda tidak cocok. Perubahan keamanan selalu meminta " +
                "kata sandi lagi.",
        statusCode: StatusCodes.Status401Unauthorized);

    private ObjectResult SessionGone() => Problem(
        title: "Akun tidak ditemukan",
        detail: "Token ini sah, tetapi akunnya sudah tidak ada. Silakan masuk kembali.",
        statusCode: StatusCodes.Status401Unauthorized);
}
