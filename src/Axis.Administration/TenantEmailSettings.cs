using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>
/// The mail settings to give a tenant. They replace the tenant's whole mail block, so an option left out is reset: a missing
/// <paramref name="Port"/> or <paramref name="UseTls"/> takes its default, and a missing <paramref name="UserName"/> or
/// <paramref name="Password"/> is empty.
/// </summary>
public sealed record TenantEmailChange(string Host, int? Port, bool? UseTls, string? UserName, SecretValue? Password, string From);

/// <summary>
/// Sets and clears the mail settings of a tenant in the tenant directory. A tenant with a host has its own mail settings, which
/// replace the host's <c>Email</c> settings as a whole. The command line and a later web console both call it, so the change
/// follows the same rules everywhere. A message names options and never a value.
/// </summary>
public static class TenantEmailSettings
{
    private const string Conflict = "Another command changed the tenant at the same moment. Run the command again.";

    /// <summary>
    /// Returns why the change is refused without reading the directory, or <see langword="null"/> when it is well formed.
    /// Only <paramref name="allowPlainSmtp"/>, which is <see langword="true"/> in Development, allows <c>--tls false</c>.
    /// </summary>
    public static string? Problem(TenantEmailChange change, bool allowPlainSmtp)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (string.IsNullOrWhiteSpace(change.Host))
        {
            return "--host must not be blank.";
        }

        if (string.IsNullOrWhiteSpace(change.From))
        {
            return "--from must not be blank.";
        }

        if (change.Port is < 1 or > 65535)
        {
            return "--port must be between 1 and 65535.";
        }

        if (change.UseTls == false && !allowPlainSmtp)
        {
            return "--tls false is allowed only in Development.";
        }

        if (change.Password is not null && change.Password.Reveal().Length == 0)
        {
            return "The SMTP password must not be empty.";
        }

        if (change.Password is not null && string.IsNullOrWhiteSpace(change.UserName))
        {
            return "A password needs --user-name.";
        }

        if (change.UserName is not null && string.IsNullOrWhiteSpace(change.UserName))
        {
            return "--user-name must not be blank.";
        }

        return null;
    }

    /// <summary>
    /// Gives the tenant its own mail settings, replacing any it had. The password is stored encrypted, bound to the tenant. The
    /// audit record names the settings that were given, in a fixed order, and never a value. A preview tenant has its own.
    /// Only <paramref name="allowPlainSmtp"/>, which is <see langword="true"/> in Development, allows the change to turn TLS off.
    /// </summary>
    public static async Task<TenantSettingResult> SetAsync(
        PlatformDbContext platform,
        TenantSecretCipher? cipher,
        string command,
        string tenantId,
        TenantEmailChange change,
        bool allowPlainSmtp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(change);

        var problem = Problem(change, allowPlainSmtp);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        if (change.Password is not null)
        {
            ArgumentNullException.ThrowIfNull(cipher);
        }

        problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        var changed = new List<string> { "smtpHost" };
        AddIf(changed, "smtpPort", change.Port is not null);
        AddIf(changed, "smtpUseTls", change.UseTls is not null);
        AddIf(changed, "smtpUserName", change.UserName is not null);
        AddIf(changed, "smtpPassword", change.Password is not null);
        changed.Add("from");

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            changed,
            [tenantId],
            async token =>
            {
                var row = await platform.Tenants.SingleAsync(tenant => tenant.Id == tenantId, token);
                row.SmtpHost = change.Host;
                row.SmtpPort = change.Port;
                row.SmtpUseTls = change.UseTls;
                row.SmtpUserName = change.UserName;
                row.SmtpPassword = change.Password is null ? null : cipher!.Encrypt(tenantId, TenantSecretFields.SmtpPassword, change.Password.Reveal());
                row.EmailFrom = change.From;
                return null;
            },
            Conflict,
            cancellationToken);
    }

    /// <summary>Removes the tenant's own mail settings, so the tenant uses the host's. A tenant that has none is cleared again without error.</summary>
    public static async Task<TenantSettingResult> ClearAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            ["email"],
            [tenantId],
            async token =>
            {
                var row = await platform.Tenants.SingleAsync(tenant => tenant.Id == tenantId, token);
                row.SmtpHost = null;
                row.SmtpPort = null;
                row.SmtpUseTls = null;
                row.SmtpUserName = null;
                row.SmtpPassword = null;
                row.EmailFrom = null;
                return null;
            },
            Conflict,
            cancellationToken);
    }

    private static void AddIf(List<string> names, string name, bool given)
    {
        if (given)
        {
            names.Add(name);
        }
    }
}
