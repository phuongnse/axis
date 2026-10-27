using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>
/// How mail is sent: the SMTP server, how to sign in to it and the address the emails come from. The password is a
/// <see cref="SecretValue"/>, so the text form never shows it. A blank value counts as unset. With no
/// <see cref="Host"/> nothing is sent.
/// </summary>
/// <param name="Host">The SMTP server's host name, or <c>null</c> when mail is off.</param>
/// <param name="Port">The SMTP port. It defaults to 587.</param>
/// <param name="UseTls">Whether to encrypt the connection: implicit TLS on port 465, otherwise STARTTLS. It defaults to <c>true</c>.</param>
/// <param name="UserName">The user to sign in as, or <c>null</c> to send without signing in.</param>
/// <param name="Password">The password of <paramref name="UserName"/>, or <c>null</c>.</param>
/// <param name="From">The sender address of every email. It is required when <paramref name="Host"/> is set.</param>
public sealed record EmailSettings(string? Host, int Port, bool UseTls, string? UserName, SecretValue? Password, string? From)
{
    /// <summary>The port when none is set.</summary>
    public const int DefaultPort = 587;

    /// <summary>
    /// Reads the mail settings from <paramref name="section"/>, whose keys are <c>Smtp:Host</c>, <c>Smtp:Port</c>,
    /// <c>Smtp:UseTls</c>, <c>Smtp:UserName</c>, <c>Smtp:Password</c> and <c>From</c>. Returns <c>null</c> when the
    /// section does not exist. A problem names the setting under <paramref name="path"/> and never holds a value.
    /// </summary>
    public static EmailSettings? Read(IConfigurationSection section, string path, ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(problems);

        if (!section.Exists())
        {
            return null;
        }

        var port = DefaultPort;
        if (Blank(section["Smtp:Port"]) is { } portText
            && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
        {
            problems.Add($"'{path}:Smtp:Port' must be an integer from 1 to 65535.");
            port = DefaultPort;
        }

        var useTls = true;
        if (Blank(section["Smtp:UseTls"]) is { } tlsText && !bool.TryParse(tlsText, out useTls))
        {
            problems.Add($"'{path}:Smtp:UseTls' must be true or false.");
            useTls = true;
        }

        var host = Blank(section["Smtp:Host"]);
        var from = Blank(section["From"]);
        if (host is not null && from is null)
        {
            problems.Add($"'{path}:From' is required when '{path}:Smtp:Host' is set.");
        }

        return new EmailSettings(
            host,
            port,
            useTls,
            Blank(section["Smtp:UserName"]),
            Blank(section["Smtp:Password"]) is { } password ? new SecretValue(password) : null,
            from);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
