using Npgsql;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.KeycloakFixture))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real Keycloak container with the development realm, shared by the whole run. It starts the
/// first time a test needs it, so a run that needs no Keycloak never starts one. It loads the Axis theme, which the
/// realm uses as its login theme, from the test output. Build the theme first with
/// <c>npm run build:keycloak-theme --prefix web</c>. It also holds the breached-password list that the realms made by <c>TenantRealm</c> name in their password policy. The fixture removes
/// the container when the run ends. It joins the <see cref="IntegrationNetwork"/>, so it reaches the Mailpit servers of <see cref="MailpitFixture"/> by their network aliases. It also holds the organization realm <c>company</c>, which plays an
/// organization's own identity provider. A test may create and delete a realm of its own, but must not change the
/// <c>axis</c> or <c>company</c> realm. Keycloak keeps its data in a PostgreSQL container of its own, on the same network, so that a test reads its
/// <c>credential</c> table as the read-only role <see cref="CredentialsConnectionStringAsync"/> returns, which only has <c>SELECT</c> on that table.
/// </summary>
public sealed class KeycloakFixture : IAsyncDisposable
{
    public const string Image = "quay.io/keycloak/keycloak:26.8.1";
    public const string ClientId = "axis";
    public const string ClientSecret = "axis-development-secret";
    public const string Password = "axis";

    /// <summary>The bootstrap admin client of the master realm, for tests that call the admin API.</summary>
    public const string AdminClientId = "axis-admin";

    public const string AdminClientSecret = "axis-admin-development-secret";

    /// <summary>The client that startup activation signs in as, to bring the <c>axis</c> realm in step.</summary>
    public const string ActivationClientId = "axis-activation";

    public const string ActivationClientSecret = "axis-activation-development-secret";

    /// <summary>The client that the users list signs in as, to read the realm's users.</summary>
    public const string UsersClientId = "axis-users";

    public const string UsersClientSecret = "axis-users-development-secret";
    public const string PurchasingClientId = "purchasing-system";
    public const string PurchasingClientSecret = "purchasing-system-development-secret";
    public const string ReportingClientId = "reporting-system";
    public const string ReportingClientSecret = "reporting-system-development-secret";

    /// <summary>The confidential client of the <c>company</c> realm that the <c>axis</c> realm brokers sign-in through.</summary>
    public const string CompanyBrokerClientId = "axis-broker";

    public const string CompanyBrokerClientSecret = "axis-broker-development-secret";

    /// <summary>The client whose tokens carry the audience <c>https://localhost/mcp</c>, for the MCP server's tests.</summary>
    public const string McpClientId = "axis-mcp-test";

    public const string McpClientSecret = "axis-mcp-test-development-secret";

    /// <summary>A second client with the same audience, so a test can call the MCP server as another client of one tenant.</summary>
    public const string McpOtherClientId = "axis-mcp-other-test";

    public const string McpOtherClientSecret = "axis-mcp-other-test-development-secret";

    /// <summary>The client that the realm's FAPI 2.0 client policy covers. It has no secret, only the two keys below.</summary>
    public const string FapiClientId = "axis-fapi";

    /// <summary>The read-only login role of Keycloak's database, for the backup of a realm's credentials. It is for tests only.</summary>
    public const string ReaderRole = "keycloak_reader";

    public const string ReaderPassword = "keycloak-reader-development-password";

    private const string DatabaseAlias = "keycloak-db";
    private const string DatabaseName = "keycloak";

    // The start takes no test's cancellation token, so cancelling one test does not fail the other.
    private readonly Lazy<Task<Containers>> _container = new(StartContainersAsync);

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
        var containers = await _container.Value;
        return new Uri(new Uri(containers.Keycloak.GetBaseAddress()), "realms/axis");
    }

    /// <summary>Starts the container if no test has yet, and returns its base address, which serves every realm and the admin API.</summary>
    public async Task<Uri> BaseAddressAsync()
    {
        var containers = await _container.Value;
        return new Uri(containers.Keycloak.GetBaseAddress());
    }

    /// <summary>Starts the containers if no test has yet, and returns the connection string of <see cref="ReaderRole"/> to Keycloak's database.</summary>
    public async Task<string> CredentialsConnectionStringAsync()
    {
        var containers = await _container.Value;
        return new NpgsqlConnectionStringBuilder(containers.Database.GetConnectionString())
        {
            Username = ReaderRole,
            Password = ReaderPassword,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    private static async Task<Containers> StartContainersAsync()
    {
        var theme = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "KeycloakTheme"));
        if (!File.Exists(Path.Combine(theme.FullName, "axis", "login", "login.ftl")))
        {
            throw new InvalidOperationException(
                "The Axis Keycloak theme is missing from the test output. Run 'npm run build:keycloak-theme --prefix web' and build the tests again.");
        }

        var network = await IntegrationNetwork.GetAsync();

        var database = new PostgreSqlBuilder(PostgreSqlServer.Image)
            .WithNetwork(network)
            .WithNetworkAliases(DatabaseAlias)
            .WithDatabase(DatabaseName)
            .WithUsername(DatabaseName)
            .WithPassword(DatabaseName)
            .Build();

        // The builder already runs start-dev, and the command adds --import-realm to it.
        var keycloak = new KeycloakBuilder(Image)
            .WithNetwork(network)
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-realm.json")), "/opt/keycloak/data/import/")
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "company-realm.json")), "/opt/keycloak/data/import/")
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-breached-passwords.txt")), "/opt/keycloak/data/password-blacklists/")
            .WithResourceMapping(theme, "/opt/keycloak/themes/")
            .WithCommand("--import-realm")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_ID", AdminClientId)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_CLIENT_SECRET", AdminClientSecret)
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment("KC_DB_URL", $"jdbc:postgresql://{DatabaseAlias}:5432/{DatabaseName}")
            .WithEnvironment("KC_DB_USERNAME", DatabaseName)
            .WithEnvironment("KC_DB_PASSWORD", DatabaseName)
            .Build();
        var containers = new Containers(keycloak, database);
        try
        {
            await database.StartAsync();
            await keycloak.StartAsync();

            // Keycloak has made its schema once it has started, so the role can be granted on its table now.
            await using var connection = new NpgsqlConnection(database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                $"CREATE ROLE {ReaderRole} LOGIN PASSWORD '{ReaderPassword}'; GRANT CONNECT ON DATABASE {DatabaseName} TO {ReaderRole}; GRANT USAGE ON SCHEMA public TO {ReaderRole}; GRANT SELECT ON credential TO {ReaderRole};",
                connection);
            await command.ExecuteNonQueryAsync();
            return containers;
        }
        catch
        {
            await containers.DisposeAsync();
            throw;
        }
    }

    // Keycloak goes first, because it holds connections to the database.
    private sealed record Containers(KeycloakContainer Keycloak, PostgreSqlContainer Database) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Keycloak.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
