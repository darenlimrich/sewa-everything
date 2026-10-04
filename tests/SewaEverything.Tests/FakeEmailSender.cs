using System.Text.RegularExpressions;
using SewaEverything.Infrastructure.Email;

namespace SewaEverything.Tests;

public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public Exception? FailWith { get; set; }

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_sent)
        {
            _sent.Clear();
        }
    }

    public IReadOnlyList<EmailMessage> For(string address) =>
        Sent.Where(m => string.Equals(m.ToAddress, address, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public EmailMessage LastFor(string address) =>
        For(address).LastOrDefault()
        ?? throw new InvalidOperationException($"Tidak ada surel yang terkirim ke {address}.");

    public string TokenFor(string address) => TokenFrom(LastFor(address));

    public static string TokenFrom(EmailMessage message)
    {
        var cocok = Regex.Match(message.TextBody, "token=([A-Za-z0-9_%\\-]+)");

        return cocok.Success
            ? Uri.UnescapeDataString(cocok.Groups[1].Value)
            : throw new InvalidOperationException(
                "Tautan atur ulang tidak ditemukan di badan surel.");
    }
}
