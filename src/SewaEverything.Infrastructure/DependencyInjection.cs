using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Bookings;
using SewaEverything.Infrastructure.Email;
using SewaEverything.Infrastructure.Payments;
using SewaEverything.Infrastructure.Persistence;
using SewaEverything.Infrastructure.Security;
using SewaEverything.Infrastructure.Storage;

namespace SewaEverything.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSewaInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres belum diatur. Isi di appsettings.Development.json " +
                "atau environment variable ConnectionStrings__Postgres.");

        services.AddDbContext<SewaDbContext>(o => o.UseNpgsql(connectionString));

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => o.SigningKeyByteLength >= 32,
                "Jwt:SigningKey minimal 32 byte — syarat HMAC-SHA256.")
            .ValidateOnStart();

        services.AddOptions<PhotoStorageOptions>()
            .Bind(configuration.GetSection(PhotoStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<HoldSweeperOptions>()
            .Bind(configuration.GetSection(HoldSweeperOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ReturnDueSweeperOptions>()
            .Bind(configuration.GetSection(ReturnDueSweeperOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RefreshTokenSweeperOptions>()
            .Bind(configuration.GetSection(RefreshTokenSweeperOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoginLockoutOptions>()
            .Bind(configuration.GetSection(LoginLockoutOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection(PasswordResetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TotpOptions>()
            .Bind(configuration.GetSection(TotpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
            ?? new EmailOptions();

        if (email.IsConfigured)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        services.AddScoped<HoldSweeper>();
        services.AddHostedService<HoldSweeperService>();

        services.AddScoped<ReturnDueSweeper>();
        services.AddHostedService<ReturnDueSweeperService>();

        services.AddScoped<RefreshTokenSweeper>();
        services.AddHostedService<RefreshTokenSweeperService>();

        services.AddOptions<SecretProtectionOptions>()
            .Bind(configuration.GetSection(SecretProtectionOptions.SectionName));

        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();

        services.AddOptions<MidtransOptions>()
            .Bind(configuration.GetSection(MidtransOptions.SectionName));

        services.AddScoped<IMidtransCredentials, PlatformMidtransCredentials>();

        services.AddHttpClient<IPaymentGateway, MidtransPaymentGateway>(c =>
            c.Timeout = TimeSpan.FromSeconds(30));

        services.AddScoped<PaymentLedger>();
        services.AddScoped<BookingCompletion>();
        services.AddScoped<MidtransWebhookProcessor>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<RefreshTokenService>();

        services.AddScoped<PasswordResetService>();

        services.AddScoped<TotpService>();

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();

        services.AddSingleton<LocalDiskPhotoStorage>();
        services.AddSingleton<IPhotoStorage>(sp => sp.GetRequiredService<LocalDiskPhotoStorage>());

        return services;
    }
}
