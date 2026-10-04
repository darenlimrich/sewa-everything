using System.ComponentModel.DataAnnotations;

namespace SewaEverything.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [StringLength(254)]
    public string FromAddress { get; set; } = string.Empty;

    [StringLength(120)]
    public string FromName { get; set; } = "Sewaku";

    public SmtpOptions Smtp { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Smtp.Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class SmtpOptions
{
    [StringLength(253)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Email:Smtp:Port harus antara 1 dan 65535.")]
    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    [StringLength(254)]
    public string Username { get; set; } = string.Empty;

    [StringLength(256)]
    public string Password { get; set; } = string.Empty;

    [Range(1, 120, ErrorMessage = "Email:Smtp:TimeoutSeconds harus antara 1 dan 120.")]
    public int TimeoutSeconds { get; set; } = 20;
}
