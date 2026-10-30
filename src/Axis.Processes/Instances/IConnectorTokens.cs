using Axis.Configuration.Model;
using Axis.Tenancy;

namespace Axis.Processes.Instances;

/// <summary>
/// Gives the access token of a connector with <c>oauth2</c> auth. The worker asks it before each
/// connector call or lookup and drops a token the system rejected with <c>401</c>, and a test scenario replaces it, so a scenario requests no token.
/// </summary>
internal interface IConnectorTokens
{
    /// <summary>
    /// Gets the token the tenant's client credentials earn for <paramref name="connector"/>, from the
    /// worker's memory or from the token endpoint at <paramref name="tokenUrl"/>.
    /// </summary>
    Task<ConnectorTokenResult> GetAsync(
        string tenantId,
        ConnectorModel connector,
        Uri tokenUrl,
        SecretValue clientId,
        SecretValue clientSecret,
        CancellationToken cancellationToken);

    /// <summary>Drops the cached token of the tenant and connector when it is still <paramref name="token"/>.</summary>
    void Drop(string tenantId, ConnectorModel connector, SecretValue token);
}

/// <summary>
/// The token, or the failure of the token request: the status the token endpoint answered with, when
/// it answered, and a fixed <paramref name="Error"/> text that holds no body and no exception message.
/// </summary>
internal sealed record ConnectorTokenResult(SecretValue? Token, int? Status, string? Error);
