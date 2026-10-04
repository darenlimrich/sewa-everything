namespace SewaEverything.Infrastructure.Email;

public sealed record EmailMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string TextBody,
    string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
