using Testcontainers.Keycloak;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.KeycloakFixture))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real Keycloak container with the development realm, shared by the whole run. It starts the
/// first time a test needs it, so a run that needs no Keycloak never starts one. It loads the Axis theme, which the
/// realm uses as its login theme, from the test output. Build the theme first with
/// <c>npm run build:keycloak-theme --prefix web</c>. It also holds the breached-password list that the realms made by <c>TenantRealm</c> name in their password policy. The fixture removes
/// the container when the run ends. A test may create and delete a realm of its own, but must not change the
/// <c>axis</c> realm.
/// </summary>
public sealed class KeycloakFixture : IAsyncDisposable
{
    public const string Image = "quay.io/keycloak/keycloak:26.4";
    public const string ClientId = "axis";
    public const string ClientSecret = "axis-development-secret";
    public const string Password = "axis";

    /// <summary>The bootstrap admin client of the master realm, for tests that call the admin API.</summary>
    public const string AdminClientId = "axis-admin";

    public const string AdminClientSecret = "axis-admin-development-secret";

    /// <summary>The client that startup activation signs in as, to bring the <c>axis</c> realm in step.</summary>
    public const string ActivationClientId = "axis-activation";

    public const string ActivationClientSecret = "axis-activation-development-secret";
    public const string PurchasingClientId = "purchasing-system";
    public const string PurchasingClientSecret = "purchasing-system-development-secret";
    public const string ReportingClientId = "reporting-system";
    public const string ReportingClientSecret = "reporting-system-development-secret";

    /// <summary>The client that the realm's FAPI 2.0 client policy covers. It has no secret, only the two keys below.</summary>
    public const string FapiClientId = "axis-fapi";

    // The start takes no test's cancellation token, so cancelling one test does not fail the other.
    private readonly Lazy<Task<KeycloakContainer>> _container = new(StartContainerAsync);

    /// <summary>
    /// The private key of <c>axis-fapi</c> with the key id <paramref name="keyId"/>, <c>key-1</c> or <c>key-2</c>, as a PEM.
    /// Both keys are for development and tests only. Their public keys are in the client's JWKS in the realm, and
    /// the keys are registered nowhere else.
    /// </summary>
    public static string FapiClientKey(string keyId) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Keycloak", $"axis-fapi-{keyId}.pem"));

    /// <summary>Starts the container if no test has yet, and returns the issuer of the axis realm.</summary>
    public async Task<Uri> StartAsync()
    {
        var container = await _container.Value;
        return new Uri(new Uri(container.GetBaseAddress()), "realms/axis");
    }

    /// <summary>Starts the container if no test has yet, and returns its base address, which serves every realm and the admin API.</summary>
    public async Task<Uri> BaseAddressAsync()
    {
        var container = await _container.Value;
        return new Uri(container.GetBaseAddress());
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
        var theme = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "KeycloakTheme"));
        if (!File.Exists(Path.Combine(theme.FullName, "axis", "login", "login.ftl")))
        {
            throw new InvalidOperationException(
                "The Axis Keycloak theme is missing from the test output. Run 'npm run build:keycloak-theme --prefix web' and build the tests again.");
        }

        // The builder already runs start-dev, and the command adds --import-realm to it.
        var container = new KeycloakBuilder(Image)
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-realm.json")), "/opt/keycloak/data/import/")
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-breached-passwords.txt")), "/opt/keycloak/data/password-blacklists/")
            .WithResourceMapping(theme, "/opt/keycloak/themes/")
            .WithCommand("--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_ID", AdminClientId)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_SECRET", AdminClientSecret)
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
