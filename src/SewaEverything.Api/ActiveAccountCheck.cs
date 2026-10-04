using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using SewaEverything.Infrastructure.Persistence;

namespace SewaEverything.Api;

internal static class ActiveAccountCheck
{
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.UserIdOrNull();

        if (userId is null)
        {
            context.Fail("Token tidak membawa identitas yang sah.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<SewaDbContext>();

        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DeactivatedAt })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (account is null)
        {
            context.Fail("Akun pemegang token sudah tidak ada.");
            return;
        }

        if (account.DeactivatedAt is not null)
        {
            context.Fail("Akses akun ini sudah dicabut.");
        }
    }
}
