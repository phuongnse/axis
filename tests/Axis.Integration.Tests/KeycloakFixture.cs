using Testcontainers.Keycloak;

namespace Axis.Integration.Tests;

/// <summary>Starts one real Keycloak container with the development realm, shared by the tests in a class.</summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    public const string Image = "quay.io/keycloak/keycloak:26.4";
    public const string ClientId = "axis";
    public const string ClientSecret = "axis-development-secret";
    public const string Password = "axis";

    // The builder already runs start-dev, and the command adds --import-realm to it.
    private readonly KeycloakContainer _container = new KeycloakBuilder(Image)
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-realm.json")), "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .Build();

    /// <summary>The issuer of the axis realm.</summary>
    public Uri Authority => new(new Uri(_container.GetBaseAddress()), "realms/axis");

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
