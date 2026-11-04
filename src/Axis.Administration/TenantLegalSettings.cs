using Axis.Configuration.Resources;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Administration;

/// <summary>
/// The legal links to give a tenant, or one of its sites when <paramref name="SitePath"/> is given. They replace the whole block,
/// so an option left out is cleared. <paramref name="TermsVersion"/> belongs to the tenant's terms, so a site has none.
/// </summary>
public sealed record TenantLegalChange(string? PrivacyUrl, string? TermsUrl, string? TermsVersion, string? SitePath);

/// <summary>
/// Sets and clears the legal links of a tenant, and of its sites, in the tenant directory: a privacy address, and a terms address
/// with the version of the terms. The command line and a later web console both call it, so the change follows the same rules
/// everywhere. A message names options and never a value.
/// </summary>
public static class TenantLegalSettings
{
    private const string Conflict = "Another command changed the tenant at the same moment. Run the command again.";

    /// <summary>Returns why the change is refused without reading the directory, or <see langword="null"/> when it is well formed.</summary>
    public static string? Problem(TenantLegalChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.PrivacyUrl is not null && string.IsNullOrWhiteSpace(change.PrivacyUrl))
        {
            return "--privacy-url must not be blank.";
        }

        if (change.TermsUrl is not null && string.IsNullOrWhiteSpace(change.TermsUrl))
        {
            return "--terms-url must not be blank.";
        }

        if (change.TermsVersion is not null && string.IsNullOrWhiteSpace(change.TermsVersion))
        {
            return "--terms-version must not be blank.";
        }

        if (change.PrivacyUrl is null && change.TermsUrl is null)
        {
            return "Give --privacy-url or --terms-url. To remove the links, use tenant legal clear.";
        }

        if (change.PrivacyUrl is not null && !IsHttps(change.PrivacyUrl))
        {
            return "--privacy-url must be an absolute https address.";
        }

        if (change.TermsUrl is not null && !IsHttps(change.TermsUrl))
        {
            return "--terms-url must be an absolute https address.";
        }

        if (change.TermsVersion is not null && change.SitePath is not null)
        {
            return "--terms-version cannot be used with --site.";
        }

        if (change.TermsUrl is not null && change.TermsVersion is null && change.SitePath is null)
        {
            return "--terms-url needs --terms-version.";
        }

        if (change.TermsVersion is not null && change.TermsUrl is null)
        {
            return "--terms-version needs --terms-url.";
        }

        return change.SitePath is null ? null : SiteProblem(change.SitePath, out _);
    }

    /// <summary>
    /// Gives the tenant, or its site, the legal links, replacing any it had. The audit record names the options that were given, in
    /// a fixed order, then the site, and never a value. A site does not need to exist: its addresses wait for it.
    /// </summary>
    public static async Task<TenantSettingResult> SetAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        TenantLegalChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(change);

        var problem = Problem(change);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        string? path = null;
        if (change.SitePath is not null)
        {
            SiteProblem(change.SitePath, out path);
        }

        var changed = new List<string>();
        AddIf(changed, "privacyUrl", change.PrivacyUrl is not null);
        AddIf(changed, "termsUrl", change.TermsUrl is not null);
        AddIf(changed, "termsVersion", change.TermsVersion is not null);
        AddIf(changed, $"site:{path}", path is not null);

        var privacyUrl = change.PrivacyUrl?.Trim();
        var termsUrl = change.TermsUrl?.Trim();
        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            changed,
            [tenantId],
            async token =>
            {
                if (path is null)
                {
                    var row = await platform.Tenants.SingleAsync(tenant => tenant.Id == tenantId, token);
                    row.PrivacyUrl = privacyUrl;
                    row.TermsUrl = termsUrl;
                    row.TermsVersion = change.TermsVersion?.Trim();
                    return null;
                }

                var site = await platform.TenantSiteLegalLinks.SingleOrDefaultAsync(link => link.TenantId == tenantId && link.SitePath == path, token);
                if (site is null)
                {
                    site = new TenantSiteLegalRow { TenantId = tenantId, SitePath = path };
                    platform.TenantSiteLegalLinks.Add(site);
                }

                site.PrivacyUrl = privacyUrl;
                site.TermsUrl = termsUrl;
                return null;
            },
            Conflict,
            cancellationToken);
    }

    /// <summary>
    /// Removes the legal links of the tenant, or of one of its sites. A tenant or site that has none is cleared again without error.
    /// </summary>
    public static async Task<TenantSettingResult> ClearAsync(
        PlatformDbContext platform,
        string command,
        string tenantId,
        string? sitePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        string? path = null;
        if (sitePath is not null)
        {
            var siteProblem = SiteProblem(sitePath, out path);
            if (siteProblem is not null)
            {
                return TenantSettingResult.Refused(siteProblem);
            }
        }

        var problem = await TenantSettingWrites.ChangeProblemAsync(platform, tenantId, cancellationToken);
        if (problem is not null)
        {
            return TenantSettingResult.Refused(problem);
        }

        return await TenantSettingWrites.WriteAsync(
            platform,
            command,
            tenantId,
            path is null ? ["legal"] : ["legal", $"site:{path}"],
            [tenantId],
            async token =>
            {
                if (path is null)
                {
                    var row = await platform.Tenants.SingleAsync(tenant => tenant.Id == tenantId, token);
                    row.PrivacyUrl = null;
                    row.TermsUrl = null;
                    row.TermsVersion = null;
                    return null;
                }

                var site = await platform.TenantSiteLegalLinks.SingleOrDefaultAsync(link => link.TenantId == tenantId && link.SitePath == path, token);
                if (site is not null)
                {
                    platform.TenantSiteLegalLinks.Remove(site);
                }

                return null;
            },
            Conflict,
            cancellationToken);
    }

    // The same rules as tenant host add --site.
    private static string? SiteProblem(string sitePath, out string path)
    {
        path = sitePath.Trim().ToLowerInvariant();
        if (!SitePaths.IsValid(path))
        {
            return $"The site path '{sitePath.Trim()}' is not a valid path.";
        }

        return SitePaths.Reserved.Contains(path) ? $"The site path '{path}' is reserved." : null;
    }

    private static bool IsHttps(string address) =>
        Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static void AddIf(List<string> names, string name, bool given)
    {
        if (given)
        {
            names.Add(name);
        }
    }
}
