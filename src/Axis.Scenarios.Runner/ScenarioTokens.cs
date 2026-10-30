using Axis.Configuration.Model;
using Axis.Processes.Instances;
using Axis.Tenancy;

namespace Axis.Scenarios.Runner;

/// <summary>
/// The access tokens of a scenario run. A connector with <c>oauth2</c> auth gets a placeholder token and
/// no token request is sent, because the scenario's stubs answer the connector's operations.
/// </summary>
internal sealed class ScenarioTokens : IConnectorTokens
{
    private static readonly SecretValue _token = new("scenario");

    public Task<ConnectorTokenResult> GetAsync(
        string tenantId,
        ConnectorModel connector,
        Uri tokenUrl,
        SecretValue clientId,
        SecretValue clientSecret,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectorTokenResult(_token, null, null));

    public void Drop(string tenantId, ConnectorModel connector, SecretValue token)
    {
        // The placeholder token is never cached, so there is nothing to drop.
    }
}
