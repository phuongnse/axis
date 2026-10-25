using Testcontainers.Keycloak;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.KeycloakFixture))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real Keycloak container with the development realm, shared by the whole run. It starts the
/// first time a test needs it, so a run that needs no Keycloak never starts one. The fixture removes
/// the container when the run ends. A test that changes realm state must not use it.
/// </summary>
public sealed class KeycloakFixture : IAsyncDisposable
{
    public const string Image = "quay.io/keycloak/keycloak:26.4";
    public const string ClientId = "axis";
    public const string ClientSecret = "axis-development-secret";
    public const string Password = "axis";
    public const string PurchasingClientId = "purchasing-system";
    public const string PurchasingClientSecret = "purchasing-system-development-secret";
    public const string ReportingClientId = "reporting-system";
    public const string ReportingClientSecret = "reporting-system-development-secret";

    // The start takes no test's cancellation token, so cancelling one test does not fail the other.
    private readonly Lazy<Task<KeycloakContainer>> _container = new(StartContainerAsync);

    /// <summary>Starts the container if no test has yet, and returns the issuer of the axis realm.</summary>
    public async Task<Uri> StartAsync()
    {
        var container = await _container.Value;
        return new Uri(new Uri(container.GetBaseAddress()), "realms/axis");
    }

    public async ValueTask DisposeAsync()
    {
        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    private static async Task<KeycloakContainer> StartContainerAsync()
    {
        // The builder already runs start-dev, and the command adds --import-realm to it.
        var container = new KeycloakBuilder(Image)
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-realm.json")), "/opt/keycloak/data/import/")
            .WithCommand("--import-realm")
            .Build();
        try
        {
            await container.StartAsync();
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }
}
