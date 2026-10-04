using Microsoft.Extensions.Logging;

namespace SewaEverything.Infrastructure.Email;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        logger.LogWarning(
            "SMTP belum dikonfigurasi, surel TIDAK terkirim. Isinya dicatat di sini supaya alurnya " +
            "tetap dapat diselesaikan saat pengembangan.\nKepada : {Kepada}\nPerihal: {Perihal}\n{Isi}",
            message.ToAddress, message.Subject, message.TextBody);

        return Task.CompletedTask;
    }
}
