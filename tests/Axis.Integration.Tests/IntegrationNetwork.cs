using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;

namespace Axis.Integration.Tests;

/// <summary>
/// One Docker network that Keycloak and both Mailpit servers join, so that Keycloak reaches a mail server by its
/// network alias, as a realm's mail server needs. It is created the first time a fixture needs it, and Testcontainers removes it when the run ends.
/// </summary>
internal static class IntegrationNetwork
{
    private static readonly Lazy<Task<INetwork>> _network = new(CreateAsync);

    public static Task<INetwork> GetAsync() => _network.Value;

    private static async Task<INetwork> CreateAsync()
    {
        var network = new NetworkBuilder().Build();
        await network.CreateAsync();
        return network;
    }
}
