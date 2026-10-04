using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace SewaEverything.Infrastructure.Email;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Email:Smtp:Host atau Email:FromAddress belum diisi, jadi tidak ada surel yang " +
                "dapat dikirim.");
        }

        using var smtp = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl             = _options.Smtp.UseStartTls,
            DeliveryMethod        = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout               = _options.Smtp.TimeoutSeconds * 1000
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
        {
            smtp.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);
        }

        using var mail = new MailMessage
        {
            From            = new MailAddress(_options.FromAddress, _options.FromName, Encoding.UTF8),
            Subject         = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body            = message.TextBody,
            BodyEncoding    = Encoding.UTF8,
            IsBodyHtml      = false
        };

        mail.To.Add(new MailAddress(message.ToAddress, message.ToName, Encoding.UTF8));

        using var html = AlternateView.CreateAlternateViewFromString(
            message.HtmlBody, Encoding.UTF8, "text/html");

        mail.AlternateViews.Add(html);

        await smtp.SendMailAsync(mail, ct);
    }
}
