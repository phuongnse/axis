namespace Axis.Tenancy.Realms;

/// <summary>
/// The desired state of the confidential client of a customer-hosted package runner in a tenant's realm, which the create route of a
/// package runner makes through <c>axis-activation</c> and the revoke route deletes
/// (<c>docs/reference/user-administration.md#package-runners</c>). The client has service accounts on, every browser flow off and an
/// audience mapper that puts <see cref="Audience"/> into its access tokens. Its id does not start with <see cref="RealmSync.SiteClientPrefix"/>,
/// so the realm sync never touches it.
/// </summary>
public static class RealmPackageRunnerClient
{
    /// <summary>The start of the client id of a package runner.</summary>
    public const string ClientIdPrefix = "runner-";

    /// <summary>The audience of the runner's access tokens (<c>docs/reference/extension-packages.md#customer-hosted-runners</c>).</summary>
    public const string Audience = "axis-package-runner";

    /// <summary>The name of the mapper that puts <see cref="Audience"/> into the access token.</summary>
    public const string AudienceMapperName = "axis-package-runner audience";

    /// <summary>The client id of the runner with <paramref name="id"/>: <see cref="ClientIdPrefix"/> and the id as 32 hexadecimal characters.</summary>
    public static string ClientId(Guid id) => $"{ClientIdPrefix}{id:N}";

    /// <summary>The client representation, with the <paramref name="secret"/> the server made.</summary>
    public static Dictionary<string, object?> Representation(string clientId, string secret) => new()
    {
        ["clientId"] = clientId,
        ["enabled"] = true,
        ["protocol"] = "openid-connect",
        ["publicClient"] = false,
        ["clientAuthenticatorType"] = "client-secret",
        ["secret"] = secret,
        ["standardFlowEnabled"] = false,
        ["directAccessGrantsEnabled"] = false,
        ["implicitFlowEnabled"] = false,
        ["serviceAccountsEnabled"] = true,
        ["fullScopeAllowed"] = false,
        ["consentRequired"] = false,
        ["protocolMappers"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["name"] = AudienceMapperName,
                ["protocol"] = "openid-connect",
                ["protocolMapper"] = "oidc-audience-mapper",
                ["config"] = new Dictionary<string, string>
                {
                    ["included.custom.audience"] = Audience,
                    ["access.token.claim"] = "true",
                    ["id.token.claim"] = "false",
                    ["introspection.token.claim"] = "true",
                },
            },
        },
    };
}
