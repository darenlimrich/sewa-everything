using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SewaEverything.Infrastructure.Email;
using SewaEverything.Infrastructure.Payments;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SewaEverything.Contracts;
using SewaEverything.Domain;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Bookings;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OwnerEmail    = "owner@test.local";
    public const string OwnerPassword = "Kunci#Rahasia#26";

    public const string Password = "KataSandi#2026";

    public string PhotoRoot { get; } =
        Path.Combine(Path.GetTempPath(), "sewa-everything-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync() => await TestDatabase.RecreateAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (Directory.Exists(PhotoRoot))
        {
            Directory.Delete(PhotoRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Postgres", TestDatabase.ConnectionString);

        builder.UseSetting("Jwt:Issuer", "sewa-everything");
        builder.UseSetting("Jwt:Audience", "sewa-everything");
        builder.UseSetting("Jwt:SigningKey", "kunci-penanda-tangan-khusus-test-minimal-32-byte");
        builder.UseSetting("Jwt:AccessTokenHours", "12");

        builder.UseSetting("Seed:Owner:Name", "Owner Test");
        builder.UseSetting("Seed:Owner:Email", OwnerEmail);
        builder.UseSetting("Seed:Owner:Password", OwnerPassword);

        builder.UseSetting("Bookings:HoldSweeper:Enabled", "false");

        builder.UseSetting("Bookings:ReturnDueSweeper:Enabled", "false");

        builder.UseSetting("Auth:RefreshTokenSweeper:Enabled", "false");

        builder.UseSetting("Security:RateLimiting", "false");

        builder.UseSetting("Security:SecretKey", SecretKey);

        builder.UseSetting("Storage:Photos:RootPath", PhotoRoot);
        builder.UseSetting("Storage:Photos:MaxBytes", MaxPhotoBytes.ToString());
        builder.UseSetting("Storage:Photos:MaxPhotosPerItem", MaxPhotosPerItem.ToString());

        builder.UseSetting("Midtrans:ServerKey", MidtransServerKey);
        builder.UseSetting("Midtrans:ClientKey", "SB-Mid-client-UJI");
        builder.UseSetting("Midtrans:IsProduction", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Gateway);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    public const string SecretKey = "dWppLXNld2EtZXZlcnl0aGluZy0zMi1ieXRlLWtleSE=";

    public const string MidtransServerKey = "SB-Mid-server-KUNCI-UJI-2026";

    public FakePaymentGateway Gateway { get; } = new();

    public FakeEmailSender Email { get; } = new();

    public const long MaxPhotoBytes = 64 * 1024;

    public const int MaxPhotosPerItem = 3;

    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.local";

    public static async Task<string> BodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var fields = json.RootElement.EnumerateObject()
            .Where(p => p.Name is not "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");

        return string.Join('|', fields);
    }

    public async Task<AuthResponse> RegisterAsync(string role, string? email = null, string? phone = null)
    {
        var response = await CreateClient().PostAsJsonAsync("/auth/register", new RegisterRequest
        {
            Name     = $"Uji {role}",
            Email    = email ?? UniqueEmail(role),
            Password = Password,
            Phone    = phone,
            Role     = role
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/auth/login",
            new LoginRequest { Email = email, Password = password });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public async Task<AuthResponse> StaffLoginAsync(string email, string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/auth/staff/login",
            new LoginRequest { Email = email, Password = password });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public HttpClient ClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public async Task<HttpClient> ClientAsAsync(string role)
    {
        var auth = await RegisterAsync(role);
        return ClientWithToken(auth.AccessToken);
    }

    public async Task<HttpClient> ClientAsOwnerAsync()
    {
        var auth = await StaffLoginAsync(OwnerEmail, OwnerPassword);
        return ClientWithToken(auth.AccessToken);
    }

    public async Task<HttpClient> ClientAsAdminAsync()
    {
        var email = UniqueEmail("admin");

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            db.Users.Add(new User
            {
                Role         = UserRole.Admin,
                Name         = "Admin Test",
                Email        = email,
                PasswordHash = hasher.Hash(Password)
            });

            await db.SaveChangesAsync();
        }

        var auth = await StaffLoginAsync(email, Password);
        return ClientWithToken(auth.AccessToken);
    }

    public sealed record UserContext(HttpClient Client, Guid Id);

    public async Task<UserContext> VerifiedSellerAsync()
    {
        var seller = await SellerAsync();
        await VerifySellerAsync(seller.Id);
        await AddPayoutAccountAsync(seller.Client);
        return seller;
    }

    public async Task<UserContext> SellerAsync()
    {
        var auth = await RegisterAsync(Roles.Seller);
        return new UserContext(ClientWithToken(auth.AccessToken), auth.User.Id);
    }

    public async Task<UserContext> RenterAsync()
    {
        var auth = await RegisterAsync(Roles.Renter);
        return new UserContext(ClientWithToken(auth.AccessToken), auth.User.Id);
    }

    public async Task VerifySellerAsync(Guid sellerId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var seller = await db.Users.FirstAsync(u => u.Id == sellerId);
        seller.MarkVerified(sellerId, DateTime.UtcNow);

        await db.SaveChangesAsync();
    }

    public async Task<ItemDetailResponse> CreateItemAsync(
        HttpClient sellerClient,
        string title = "Kamera Mirrorless",
        string category = "Elektronik",
        decimal price = 150_000m,
        string priceUnit = PriceUnits.Day,
        decimal deposit = 500_000m,
        string? description = null,
        decimal? deliveryFee = null,
        bool approve = true)
    {
        var response = await sellerClient.PostAsJsonAsync("/items", new CreateItemRequest
        {
            Title         = title,
            Category      = category,
            Description   = description,
            Price         = price,
            PriceUnit     = priceUnit,
            DepositAmount = deposit,
            DeliveryFee   = deliveryFee
        });

        response.EnsureSuccessStatusCode();

        var item = (await response.Content.ReadFromJsonAsync<ItemDetailResponse>())!;

        if (!approve)
        {
            return item;
        }

        await ApproveItemAsync(item.Id);

        return item with { ReviewStatus = ItemReviewStatuses.Approved };
    }

    public async Task ApproveItemAsync(Guid itemId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var owner = await db.Users
            .Where(u => u.Role == UserRole.Owner)
            .Select(u => u.Id)
            .FirstAsync();

        var item = await db.Items.FirstAsync(i => i.Id == itemId);
        item.Approve(owner, DateTime.UtcNow);

        await db.SaveChangesAsync();
    }

    public async Task<string> ItemReviewStatusAsync(Guid itemId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        return await db.Items
            .Where(i => i.Id == itemId)
            .Select(i => i.ReviewStatus.ToDbValue())
            .SingleAsync();
    }

    public async Task SeedBookingAsync(
        Guid itemId, Guid renterId, DateTimeOffset from, DateTimeOffset to, string status = "pending")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO bookings (
                item_id, renter_id, during, status,
                price_snapshot, price_unit_snapshot, duration_units,
                total_rent, deposit_amount,
                platform_fee_rate, platform_fee_mode, platform_fee_amount,
                hold_expires_at)
            VALUES (
                {itemId}, {renterId},
                tstzrange({from.UtcDateTime}, {to.UtcDateTime}, '[)'), {status},
                100000, 'day', 2,
                200000, 0,
                0, 'deduct', 0,
                now() + interval '15 minutes')
            """);
    }

    public async Task<BookingResponse> CreateBookingAsync(
        HttpClient renterClient, Guid itemId, DateTimeOffset from, DateTimeOffset to)
    {
        var response = await BookAsync(renterClient, itemId, from, to);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BookingResponse>())!;
    }

    public sealed record ActiveScenario(UserContext Seller, UserContext Renter, BookingResponse Booking);

    public async Task<ActiveScenario> ActiveBookingAsync(
        DateTimeOffset from, DateTimeOffset to,
        decimal price = 100_000m, decimal deposit = 500_000m,
        string channel = PaymentChannels.Gopay)
    {
        var seller  = await VerifiedSellerAsync();
        var renter  = await RenterAsync();
        var item    = await CreateItemAsync(seller.Client, price: price, deposit: deposit);
        var booking = await CreateBookingAsync(renter.Client, item.Id, from, to);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/approve", null))
            .EnsureSuccessStatusCode();

        if (!PaymentChannels.FromDbValue(channel).IsReversible())
        {
            await AddPayoutAccountAsync(renter.Client);
        }

        await SettleAsync(renter.Client, booking.Id, channel);

        (await seller.Client.PostAsync($"/bookings/{booking.Id}/handover", null))
            .EnsureSuccessStatusCode();

        return new ActiveScenario(seller, renter, booking);
    }

    public Task<HttpResponseMessage> BookAsync(
        HttpClient renterClient, Guid itemId, DateTimeOffset from, DateTimeOffset to) =>
        renterClient.PostAsJsonAsync("/bookings", new CreateBookingRequest
        {
            ItemId   = itemId,
            StartsAt = from,
            EndsAt   = to
        });

    public async Task DriveToStatusAsync(Guid bookingId, string target)
    {
        string[] path = target switch
        {
            BookingStatuses.Pending   => [],
            BookingStatuses.Confirmed => [BookingStatuses.Confirmed],
            BookingStatuses.Cancelled => [BookingStatuses.Cancelled],
            BookingStatuses.Active    => [BookingStatuses.Confirmed, BookingStatuses.Active],
            BookingStatuses.Disputed  => [BookingStatuses.Confirmed, BookingStatuses.Active, BookingStatuses.Disputed],
            BookingStatuses.Completed => [BookingStatuses.Confirmed, BookingStatuses.Active, BookingStatuses.Completed],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "status tidak dikenal")
        };

        var current = await StatusOfAsync(bookingId);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        foreach (var step in path.Where(s => Rank(s) > Rank(current)))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE bookings SET status = {step} WHERE id = {bookingId}");

            current = step;
        }
    }

    private static int Rank(string status) => status switch
    {
        BookingStatuses.Pending   => 0,
        BookingStatuses.Confirmed => 1,
        BookingStatuses.Active    => 2,
        _                         => 3
    };

    public async Task ExpireHoldAsync(Guid bookingId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE bookings SET hold_expires_at = now() - interval '1 minute'
            WHERE id = {bookingId}
            """);
    }

    public async Task<int> SweepHoldsAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HoldSweeper>().SweepAsync();
    }

    public async Task<int> SweepReturnsAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReturnDueSweeper>().SweepAsync();
    }

    public async Task<int> SweepRefreshTokensAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshTokenSweeper>().SweepAsync();
    }

    public async Task<int> SetReturnWindowDaysAsync(int days)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        var sebelum = await db.PlatformSettings.AsNoTracking().Select(s => s.ReturnWindowDays).FirstAsync();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE platform_settings SET return_window_days = {days}");

        return sebelum;
    }

    public async Task<Guid> SeedActivePaidBookingAsync(
        Guid itemId, Guid renterId, DateTimeOffset from, DateTimeOffset to,
        decimal price = 100_000m, int durationUnits = 2, decimal deposit = 500_000m,
        decimal feeRate = 0.05m, decimal feeAmount = 10_000m,
        string channel = PaymentChannels.Gopay)
    {
        var bookingId = Guid.NewGuid();
        var totalRent = price * durationUnits;

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO bookings (
                id, item_id, renter_id, during, status,
                price_snapshot, price_unit_snapshot, duration_units,
                total_rent, deposit_amount,
                platform_fee_rate, platform_fee_mode, platform_fee_amount,
                hold_expires_at)
            VALUES (
                {bookingId}, {itemId}, {renterId},
                tstzrange({from.UtcDateTime}, {to.UtcDateTime}, '[)'), 'active',
                {price}, 'day', {durationUnits},
                {totalRent}, {deposit},
                {feeRate}, 'deduct', {feeAmount},
                NULL)
            """);

        var gatewayRef = $"SEED-{bookingId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments (booking_id, kind, direction, amount, status, method, channel,
                                  counterparty_id, gateway_ref, idempotency_key, settled_at)
            VALUES ({bookingId}, 'rent_charge', 'in', {totalRent}, 'paid', 'gateway_charge',
                    {channel}, {renterId}, {gatewayRef},
                    {$"rent_charge:{bookingId:N}:1"}, now())
            """);

        if (deposit > 0m)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO payments (booking_id, kind, direction, amount, status, method, channel,
                                      counterparty_id, gateway_ref, idempotency_key, settled_at)
                VALUES ({bookingId}, 'deposit_charge', 'in', {deposit}, 'paid', 'gateway_charge',
                        {channel}, {renterId}, {gatewayRef},
                        {$"deposit_charge:{bookingId:N}:1"}, now())
                """);
        }

        return bookingId;
    }

    public async Task<string> StatusOfAsync(Guid bookingId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SewaDbContext>();

        return await db.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => b.Status.ToDbValue())
            .SingleAsync();
    }

    public Task<HttpResponseMessage> PayAsync(HttpClient renterClient, Guid bookingId, string channel) =>
        renterClient.PostAsJsonAsync($"/bookings/{bookingId}/pay",
            new PayBookingRequest { Channel = channel });

    public async Task<PaymentInstructionResponse> PaySuccessfullyAsync(
        HttpClient renterClient, Guid bookingId, string channel = PaymentChannels.Gopay)
    {
        var response = await PayAsync(renterClient, bookingId, channel);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PaymentInstructionResponse>())!;
    }

    public async Task<PayoutAccountResponse> AddPayoutAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/payout-accounts", new CreatePayoutAccountRequest
        {
            Kind          = PayoutAccountKinds.Bank,
            ProviderCode  = "BCA",
            AccountNumber = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(),
            AccountHolder = "Pemilik Uji"
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PayoutAccountResponse>())!;
    }

    public static string MidtransNotification(
        string orderId, decimal grossAmount, string transactionStatus,
        string? transactionId = null, string? fraudStatus = "accept",
        string? serverKey = null, string? overrideSignature = null)
    {
        var statusCode = transactionStatus switch
        {
            "settlement" or "capture" => "200",
            "pending"                 => "201",
            "expire"                  => "407",
            _                         => "202"
        };

        var gross = grossAmount.ToString("0.00", CultureInfo.InvariantCulture);

        var signature = overrideSignature ?? MidtransSignature.Compute(
            orderId, statusCode, gross, serverKey ?? MidtransServerKey);

        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["order_id"]           = orderId,
            ["status_code"]        = statusCode,
            ["gross_amount"]       = gross,
            ["signature_key"]      = signature,
            ["transaction_id"]     = transactionId ?? $"TRX-{orderId}",
            ["transaction_status"] = transactionStatus,
            ["fraud_status"]       = fraudStatus ?? "accept",
            ["payment_type"]       = "bank_transfer"
        });
    }

    public Task<HttpResponseMessage> SendWebhookAsync(string payload) =>
        CreateClient().PostAsync("/webhooks/payment",
            new StringContent(payload, Encoding.UTF8, "application/json"));

    public async Task<PaymentInstructionResponse> SettleAsync(
        HttpClient renterClient, Guid bookingId, string channel = PaymentChannels.Gopay)
    {
        var instruction = await PaySuccessfullyAsync(renterClient, bookingId, channel);

        var webhook = await SendWebhookAsync(
            MidtransNotification(instruction.OrderId, instruction.Amount, "settlement"));

        webhook.EnsureSuccessStatusCode();

        return instruction;
    }

    public async Task<List<PaymentResponse>> LedgerEntriesAsync(HttpClient client, Guid bookingId)
    {
        var ledger = await client.GetFromJsonAsync<BookingLedgerResponse>($"/bookings/{bookingId}/ledger");
        return [.. ledger!.Entries];
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
