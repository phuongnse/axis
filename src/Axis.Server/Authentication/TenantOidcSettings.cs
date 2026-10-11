using Axis.Tenancy;

namespace Axis.Server.Authentication;

/// <summary>
/// How one tenant signs people in, from the <c>Oidc</c> block of the tenant in the <c>Tenants</c>
/// section. Only the server reads it. The worker reads the same section and ignores the block.
/// </summary>
internal sealed class TenantOidcSettings
{
    public const string SectionName = "Oidc";

    /// <summary>The issuer of the tenant's identity provider.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>The names of the claims that give the user id, the display name and the roles.</summary>
    public TenantOidcClaims Claims { get; set; } = new();

    /// <summary>Reads the <c>Oidc</c> block of every tenant and checks it.</summary>
    /// <returns>The settings keyed by tenant id.</returns>
    /// <exception cref="InvalidOperationException">
    /// A tenant has no <c>Oidc</c> block, or its authority, client id or client secret is blank.
    /// </exception>
    public static IReadOnlyDictionary<string, TenantOidcSettings> Load(IConfiguration configuration)
    {
        var settings = new Dictionary<string, TenantOidcSettings>(StringComparer.Ordinal);
        var problems = new List<string>();
        var tenants = configuration.GetSection(TenantOptions.SectionName).GetChildren()
            .OrderBy(tenant => tenant.Key, StringComparer.Ordinal);
        foreach (var tenant in tenants)
        {
            var section = tenant.GetSection(SectionName);
            if (!section.Exists())
            {
                problems.Add($"Tenant '{tenant.Key}' has no Oidc block.");
                continue;
            }

            var oidc = section.Get<TenantOidcSettings>() ?? new TenantOidcSettings();
            if (string.IsNullOrWhiteSpace(oidc.Authority))
            {
                problems.Add($"Tenant '{tenant.Key}' has no Oidc authority.");
            }

            if (string.IsNullOrWhiteSpace(oidc.ClientId))
            {
                problems.Add($"Tenant '{tenant.Key}' has no Oidc client id.");
            }

            if (string.IsNullOrWhiteSpace(oidc.ClientSecret))
            {
                problems.Add($"Tenant '{tenant.Key}' has no Oidc client secret.");
            }

            settings[tenant.Key] = oidc;
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The sign-in configuration is invalid. {string.Join(" ", problems)}");
        }

        return settings;
    }
}

/// <summary>Claim names in the identity provider's ID token. A blank name falls back to its default.</summary>
internal sealed class TenantOidcClaims
{
    private string _userId = "sub";
    private string _displayName = "name";
    private string _roles = "roles";

    public string UserId
    {
        get => _userId;
        set => _userId = string.IsNullOrWhiteSpace(value) ? "sub" : value;
    }

    public string DisplayName
    {
        get => _displayName;
        set => _displayName = string.IsNullOrWhiteSpace(value) ? "name" : value;
    }

    public string Roles
    {
        get => _roles;
        set => _roles = string.IsNullOrWhiteSpace(value) ? "roles" : value;
    }
}
