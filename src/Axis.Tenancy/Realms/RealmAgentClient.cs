namespace Axis.Tenancy.Realms;

/// <summary>
/// The desired state of <c>axis-agent</c>, the agent host's confidential client in a tenant's realm, which <c>TenantRealm</c> makes and
/// <see cref="RealmSync"/> puts back. It can do standard token exchange only, and its refresh tokens end with the author's realm
/// session (<c>docs/reference/identity-provider.md#keeping-the-realm-in-step</c>).
/// </summary>
public static class RealmAgentClient
{
    /// <summary>The client id of the agent host's client.</summary>
    public const string ClientId = "axis-agent";

    /// <summary>The client attribute that turns standard token exchange on.</summary>
    public const string TokenExchangeAttribute = "standard.token.exchange.enabled";

    /// <summary>The client attribute that lets an exchange return a refresh token.</summary>
    public const string RefreshAttribute = "standard.token.exchange.enableRefreshRequestedTokenType";

    /// <summary>The value of <see cref="RefreshAttribute"/> that ties the refresh token to the author's realm session.</summary>
    public const string RefreshValue = "SAME_SESSION";

    /// <summary>The name of the mapper of the platform client <c>axis</c> that puts <see cref="ClientId"/> into the audience of its access token, so the token can be exchanged.</summary>
    public const string AudienceMapperName = "axis-agent audience";

    /// <summary>The client representation. The <paramref name="secret"/> is left out when it is <c>null</c>, which keeps the stored one.</summary>
    public static Dictionary<string, object?> Representation(string? secret)
    {
        var client = new Dictionary<string, object?>
        {
            ["clientId"] = ClientId,
            ["enabled"] = true,
            ["protocol"] = "openid-connect",
            ["publicClient"] = false,
            ["clientAuthenticatorType"] = "client-secret",
            ["standardFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = false,
            ["implicitFlowEnabled"] = false,
            ["serviceAccountsEnabled"] = false,
            ["fullScopeAllowed"] = false,
            ["consentRequired"] = false,
            ["attributes"] = Attributes(),
        };
        if (secret is not null)
        {
            client["secret"] = secret;
        }

        return client;
    }

    /// <summary>The client attributes that this class owns. Every other attribute of the client stays as it is.</summary>
    public static Dictionary<string, string> Attributes() => new(StringComparer.Ordinal)
    {
        [TokenExchangeAttribute] = "true",
        [RefreshAttribute] = RefreshValue,
    };
}
