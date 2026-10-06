using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SewaEverything.Api;
using SewaEverything.Api.Security;
using SewaEverything.Contracts;
using SewaEverything.Infrastructure;
using SewaEverything.Infrastructure.Auth;
using SewaEverything.Infrastructure.Email;
using SewaEverything.Infrastructure.Security;
using SewaEverything.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
{
    const string bawaan = "An error occurred while processing your request.";

    if (ctx.ProblemDetails.Status >= 500 && ctx.ProblemDetails.Title is null or bawaan)
    {
        ctx.ProblemDetails.Title = "Terjadi kesalahan di server";
        ctx.ProblemDetails.Detail ??=
            "Permintaanmu tidak bisa diproses. Coba lagi sebentar lagi.";
    }
});

const string webCorsPolicy = "web";
var webOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5003", "https://localhost:7012"];

builder.Services.AddCors(options => options.AddPolicy(webCorsPolicy, policy => policy
    .WithOrigins(webOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>()
    ?? new SecurityOptions();

builder.Services.AddSewaRateLimiter(security);

builder.Services.AddSewaInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Bagian konfigurasi 'Jwt' tidak ditemukan.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;

        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwt.Issuer,
            ValidateAudience         = true,
            ValidAudience            = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),

            RoleClaimType            = SewaClaims.Role,
            NameClaimType            = SewaClaims.Subject
        };

        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = ActiveAccountCheck.OnTokenValidatedAsync
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsProduction()
    && builder.Configuration["AllowedHosts"] is null or "" or "*")
{
    throw new InvalidOperationException(
        "AllowedHosts masih '*' di Production. Isi daftar host yang sah — misalnya " +
        "\"sewaku.id;www.sewaku.id\" — supaya permintaan dengan header Host palsu ditolak " +
        "sebelum menyentuh aplikasi.");
}

if (app.Environment.IsProduction()
    && builder.Configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.IsConfigured
       is not true)
{
    throw new InvalidOperationException(
        "Email:Smtp:Host dan Email:FromAddress belum diisi di Production. Tanpa keduanya, " +
        "'lupa kata sandi' menerima permintaan lalu tidak pernah mengirim apa pun — dan pengguna " +
        "yang lupa sandinya kehilangan akunnya permanen.");
}

if (security.TrustProxyHeaders)
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };

    if (security.TrustAllProxies)
    {
        forwarded.ForwardLimit = 1;
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
    }

    foreach (var proxy in security.KnownProxies)
    {
        if (System.Net.IPAddress.TryParse(proxy, out var alamat))
        {
            forwarded.KnownProxies.Add(alamat);
        }
    }

    app.UseForwardedHeaders(forwarded);
}

app.UseSewaSecurityHeaders();

if (security.RequireHttps
    && !app.Environment.IsDevelopment()
    && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

var photoOptions = app.Services.GetRequiredService<IOptions<PhotoStorageOptions>>().Value;

if (!photoOptions.UsesDatabase)
{
    var photos = app.Services.GetRequiredService<LocalDiskPhotoStorage>();

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(photos.RootPath),
        RequestPath  = photos.RequestPath,

        ServeUnknownFileTypes = false,

        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        }
    });
}

app.UseRouting();

app.UseCors(webCorsPolicy);

if (security.RateLimiting)
{
    app.UseRateLimiter();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (photoOptions.UsesDatabase)
{
    app.MapGet($"{photoOptions.RequestPath}/{{name}}", async (
        string name, HttpContext http, IPhotoStorage storage, CancellationToken ct) =>
    {
        var photo = await storage.ReadAsync(name, ct);

        if (photo is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return Results.File(photo.Content, photo.ContentType);
    }).AllowAnonymous();
}

app.MapGet("/health", () => Results.Ok()).AllowAnonymous().DisableRateLimiting();

await app.Services.ProtectStoredSecretsAsync();

await app.Services.SeedOwnerAsync(PasswordPolicy.Periksa);

await app.Services.SeedItemPhotosAsync();

app.Run();

public partial class Program;
