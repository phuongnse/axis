namespace Axis.Tenancy.Realms;

/// <summary>
/// What the realm sync writes of a tenant's legal settings: the links, and the address of the licences page. It writes
/// the links on every client of the realm and turns the Terms and Conditions step on while <see cref="TenantLegal.TermsUrl"/> is set.
/// </summary>
/// <param name="Legal">The tenant's privacy and terms links, or <c>null</c> when it sets none.</param>
/// <param name="LicencesUrl">The address of the licences page, or <c>null</c> when the tenant has no host.</param>
public sealed record RealmLegal(TenantLegal? Legal, string? LicencesUrl)
{
    /// <summary>A tenant with no legal settings and no host: the sync removes the legal attributes and turns the terms step off.</summary>
    public static readonly RealmLegal None = new(null, null);

    /// <summary>
    /// Builds the realm's legal settings. The licences page is <c>https://</c>, the first host in ordinal order that belongs to no site and
    /// <c>/licences</c>, or the first host when every host belongs to a site. Hosts are trimmed and lower-cased, and empty ones are dropped.
    /// </summary>
    /// <param name="legal">The tenant's privacy and terms links, or <c>null</c> when it sets none.</param>
    /// <param name="hosts">The tenant's hosts.</param>
    /// <param name="hostSites">The site path of each host that belongs to a site, keyed by host and matched ignoring letter case, or <c>null</c> when no host does.</param>
    public static RealmLegal Of(TenantLegal? legal, IEnumerable<string> hosts, IReadOnlyDictionary<string, string>? hostSites)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        var ordered = hosts
            .Select(host => host.Trim().ToLowerInvariant())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var chosen = ordered.FirstOrDefault(host => hostSites is null || !hostSites.Keys.Any(key => string.Equals(key.Trim(), host, StringComparison.OrdinalIgnoreCase)))
            ?? ordered.FirstOrDefault();
        return new RealmLegal(legal, chosen is null ? null : $"https://{chosen}/licences");
    }
}
