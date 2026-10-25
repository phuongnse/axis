using Axis.Tenancy;

namespace Axis.Server.Authentication;

/// <summary>
/// How one tenant signs people in, from the Oidc settings of the tenant in the tenant catalog. Only the
/// server reads them. The worker's tenants have none.
/// </summary>
internal sealed class TenantOidcSettings
{
    public const string SectionName = "Oidc";

    /// <summary>The issuer of the tenant's identity provider.</summary>
    public string? Authority { get; set; }

    public string? ClientId { get; set; }

    public SecretValue? ClientSecret { get; set; }

    /// <summary>
    /// The audience that a machine caller's bearer token must contain. A blank value means the
    /// tenant accepts no bearer token, so every endpoint call there is a <c>401</c>.
    /// </summary>
    public string? EndpointAudience { get; set; }

    /// <summary>The names of the claims that give the user id, the display name, the roles, the email and the locale.</summary>
    public TenantOidcClaims Claims { get; set; } = new();

    /// <summary>Reads the Oidc settings of one tenant and checks them.</summary>
    /// <param name="problems">Receives one message for each problem found. The messages hold no secret.</param>
    /// <returns>The settings, or null when there are problems.</returns>
    internal static TenantOidcSettings? TryCreate(TenantEntry tenant, List<string> problems)
    {
        if (tenant.Oidc is not { } source)
        {
            problems.Add($"Tenant '{tenant.Id}' has no Oidc block.");
            return null;
        }

        var count = problems.Count;
        var oidc = new TenantOidcSettings
        {
            Authority = source.Authority,
            ClientId = source.ClientId,
            ClientSecret = source.ClientSecret,
            EndpointAudience = source.EndpointAudience,
            Claims =
            {
                UserId = source.UserIdClaim ?? "",
                DisplayName = source.DisplayNameClaim ?? "",
                Roles = source.RolesClaim ?? "",
                Email = source.EmailClaim ?? "",
                Locale = source.LocaleClaim ?? "",
            },
        };
        if (string.IsNullOrWhiteSpace(oidc.Authority))
        {
            problems.Add($"Tenant '{tenant.Id}' has no Oidc authority.");
        }

        if (string.IsNullOrWhiteSpace(oidc.ClientId))
        {
            problems.Add($"Tenant '{tenant.Id}' has no Oidc client id.");
        }

        if (oidc.ClientSecret is null)
        {
            problems.Add($"Tenant '{tenant.Id}' has no Oidc client secret.");
        }

        return problems.Count == count ? oidc : null;
    }

    /// <summary>Reads the Oidc settings of every tenant and checks them.</summary>
    /// <returns>The settings keyed by tenant id.</returns>
    /// <exception cref="InvalidOperationException">
    /// A tenant has no <c>Oidc</c> block, or its authority, client id or client secret is blank.
    /// </exception>
    public static IReadOnlyDictionary<string, TenantOidcSettings> Load(ITenantCatalog catalog)
    {
        var settings = new Dictionary<string, TenantOidcSettings>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var tenant in catalog.Tenants)
        {
            if (TryCreate(tenant, problems) is { } oidc)
            {
                settings[tenant.Id] = oidc;
            }
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
    private string _email = "email";
    private string _locale = "locale";

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

    public string Email
    {
        get => _email;
        set => _email = string.IsNullOrWhiteSpace(value) ? "email" : value;
    }

    public string Locale
    {
        get => _locale;
        set => _locale = string.IsNullOrWhiteSpace(value) ? "locale" : value;
    }
}
