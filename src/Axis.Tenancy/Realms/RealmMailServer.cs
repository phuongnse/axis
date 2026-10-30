using System.Globalization;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Maps <see cref="EmailSettings"/> to the <c>smtpServer</c> of a Keycloak realm, so the realm sends its emails, such as a
/// password reset link, the way Axis does. The map holds the password in plain text, so never log or print it.
/// </summary>
public static class RealmMailServer
{
    /// <summary>
    /// The realm's mail server for <paramref name="settings"/>. TLS on port 465 is <c>ssl</c> and TLS on another port is
    /// <c>starttls</c>. The map is empty, which clears the mail server, when there are no settings or no host, and when TLS is off
    /// while <paramref name="allowPlainSmtp"/> is <c>false</c>, because only Development sends mail in plain text.
    /// </summary>
    public static Dictionary<string, string> Of(EmailSettings? settings, bool allowPlainSmtp)
    {
        if (settings?.Host is not { } host || (!settings.UseTls && !allowPlainSmtp))
        {
            return [];
        }

        var server = new Dictionary<string, string>
        {
            ["host"] = host,
            ["port"] = settings.Port.ToString(CultureInfo.InvariantCulture),
            ["from"] = settings.From ?? "",
            ["ssl"] = Flag(settings.UseTls && settings.Port == 465),
            ["starttls"] = Flag(settings.UseTls && settings.Port != 465),
            ["auth"] = Flag(settings.UserName is not null),
        };
        if (settings.UserName is { } user)
        {
            server["user"] = user;
        }

        if (settings.Password is { } password)
        {
            server["password"] = password.Reveal();
        }

        return server;
    }

    private static string Flag(bool value) => value ? "true" : "false";
}
