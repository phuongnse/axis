using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Axis.Admin;

/// <summary>
/// The mail settings that a tenant's realm falls back to, and whether plain SMTP is allowed. <paramref name="Host"/> is the host's
/// <c>Email</c> block, the one the worker reads, or <c>null</c> when this host has none. <paramref name="AllowPlainSmtp"/> is true only in Development,
/// the same rule the worker follows. The commands that write a realm use <see cref="For"/> to build the realm's mail server.
/// </summary>
internal sealed record RealmMail(EmailSettings? Host, bool AllowPlainSmtp)
{
    public const string Section = "Email";

    /// <summary>No fallback and no plain SMTP: a tenant without mail settings of its own gets no mail server.</summary>
    public static RealmMail None { get; } = new(null, false);

    /// <summary>The realm's mail server for a tenant: its own settings, or the host's when it has none. The map holds the password.</summary>
    public IReadOnlyDictionary<string, string> For(EmailSettings? tenant) => RealmMailServer.Of(tenant ?? Host, AllowPlainSmtp);

    /// <summary>
    /// Reads the <c>Email</c> block of this host. A problem names a setting and never holds a value.
    /// </summary>
    public static RealmMail Read(IConfiguration configuration, ICollection<string> problems) =>
        new(
            EmailSettings.Read(configuration.GetSection(Section), Section, problems),
            (configuration[AdminCommands.DotnetEnvironmentSetting] ?? Environments.Production) == Environments.Development);
}
