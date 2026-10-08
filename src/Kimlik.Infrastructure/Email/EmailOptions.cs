using System.ComponentModel.DataAnnotations;

namespace Kimlik.Infrastructure.Email;

/// <summary>Email delivery settings from the <c>Kimlik:Email</c> configuration section.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Kimlik:Email";

    /// <summary>Sender address. Required to send email.</summary>
    [EmailAddress]
    public string? FromAddress { get; set; }

    /// <summary>Sender display name; the product name when unset.</summary>
    public string? FromName { get; set; }

    public SmtpOptions Smtp { get; set; } = new();

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(Smtp.Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// <c>Auto</c> uses TLS when the server offers it (implicit TLS on port 465). Use <c>None</c> only for local
    /// development servers such as Mailpit.
    /// </summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;
}

public enum SmtpSecurity
{
    Auto,
    StartTls,
    SslOnConnect,
    None,
}
