using System.Text;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Core.Tenancy;
using Axis.Data;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Applications;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// A directory tenant gets its deployment bindings from its directory entry, with each secret stored encrypted.
/// The worker calls each tenant's connector with that tenant's own url and secret, and activation checks a release
/// against the bindings of the directory entry.
/// </summary>
public sealed class DirectoryTenantBindingsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string TestSecretKey = TestPlatform.SecretKey;

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_directory_tenants_connector_call_uses_its_own_url_and_secret_and_no_log_holds_a_secret()
    {
        var fixtureA = new ProcessStepFixture("a");
        var fixtureB = new ProcessStepFixture("b");
        try
        {
            await fixtureA.InitializeAsync();
            await fixtureB.InitializeAsync();
            await using var stub = await ConnectorStub.StartAsync();
            var tokens = new Dictionary<string, string>
            {
                ["a"] = $"token-a-{Guid.NewGuid():N}",
                ["b"] = $"token-b-{Guid.NewGuid():N}",
            };
            var platform = await database.CreateDatabaseAsync("bindings_platform", CancellationToken);
            await SeedPlatformAsync(
                platform,
                new SeededTenant("a", fixtureA.ConnectionString, "a.example.test", [Url("PurchasingUrl", $"{stub.BaseUrl}/tenant-a"), Secret("a", "PurchasingToken", tokens["a"])]),
                new SeededTenant("b", fixtureB.ConnectionString, "b.example.test", [Url("PurchasingUrl", $"{stub.BaseUrl}/tenant-b"), Secret("b", "PurchasingToken", tokens["b"])]));
            await GrantPlatformAsync(platform);
            var logs = new LogCollector();
            await using var app = BuildWorker(platform, logs);
            var subjectA = await fixtureA.InsertRequestAsync(500m, 1);
            var subjectB = await fixtureB.InsertRequestAsync(500m, 1);
            var instanceA = await fixtureA.StartAsync("Order", subjectA, fixtureA.FirstReleaseId);
            var instanceB = await fixtureB.StartAsync("Order", subjectB, fixtureB.FirstReleaseId);

            await app.StartAsync(CancellationToken);
            await WaitUntilAsync(
                async () => await fixtureA.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceA)) is not null
                    && await fixtureB.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceB)) is not null
                    && stub.Requests.Any(request => request.Path == "/tenant-a/orders")
                    && stub.Requests.Any(request => request.Path == "/tenant-b/orders"),
                "The connector calls did not reach the stub.",
                TimeSpan.FromSeconds(60));

            var requestA = stub.Requests.First(request => request.Path == "/tenant-a/orders");
            var requestB = stub.Requests.First(request => request.Path == "/tenant-b/orders");
            Assert.Equal(("POST", "/tenant-a/orders", $"Bearer {tokens["a"]}"), (requestA.Method, requestA.Path, requestA.Authorization));
            Assert.Equal(("POST", "/tenant-b/orders", $"Bearer {tokens["b"]}"), (requestB.Method, requestB.Path, requestB.Authorization));

            foreach (var (id, token) in tokens)
            {
                var stored = (byte[])(await PostgreSqlServer.AdminScalarAsync(
                    platform,
                    $"SELECT secret_value FROM platform.tenant_bindings WHERE tenant_id = '{id}' AND name = 'PurchasingToken'",
                    CancellationToken))!;
                Assert.False(Contains(stored, Encoding.UTF8.GetBytes(token)), "The stored secret holds the plain token.");
                Assert.NotEmpty(logs.Entries);
                Assert.All(logs.Entries, entry => Assert.DoesNotContain(token, entry, StringComparison.Ordinal));
            }
        }
        finally
        {
            await fixtureA.DisposeAsync();
            await fixtureB.DisposeAsync();
        }
    }

    [Fact]
    public async Task Activating_a_release_with_the_bindings_of_a_directory_entry_that_lacks_one_fails_naming_the_tenant_and_the_binding()
    {
        var tenantDatabase = await database.CreateDatabaseAsync("bindings_tenant", CancellationToken);
        var platform = await database.CreateDatabaseAsync("bindings_platform", CancellationToken);
        await SeedPlatformAsync(platform, new SeededTenant("a", tenantDatabase, "a.example.test", [Url("PurchasingUrl", "https://purchasing.example.test")]));
        await GrantPlatformAsync(platform);
        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(platform), _cipher, CancellationToken);
        var bindings = TenantActivationBindings.For(catalog.Find("a")!, allowHttp: false);

        await using var connection = new NpgsqlConnection(tenantDatabase);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        await configuration.Database.MigrateAsync(CancellationToken);
        await data.Database.MigrateAsync(CancellationToken);
        var compiled = await ReleaseCompiler.CompileAsync(ProcessStepFixture.StepAppFolder, configuration, CancellationToken);
        Assert.Empty(compiled.Diagnostics);

        var activated = await ReleaseActivator.ActivateAsync(
            compiled,
            new ActiveReleaseStore(configuration),
            data,
            bindings,
            OpenInstanceCounter.Instance,
            cancellationToken: CancellationToken);

        // Every connector of StepApp uses the missing secret, and each one is reported.
        Assert.NotEmpty(activated.Diagnostics);
        Assert.All(activated.Diagnostics, diagnostic =>
        {
            Assert.Equal("AXC0100", diagnostic.Code);
            Assert.Contains("Tenant 'a'", diagnostic.Message, StringComparison.Ordinal);
            Assert.Contains("PurchasingToken", diagnostic.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_storage_binding_holding_database_is_loaded_and_one_holding_another_value_fails_the_read_naming_the_tenant_and_the_binding()
    {
        var tenantDatabase = await database.CreateDatabaseAsync("bindings_tenant", CancellationToken);
        var good = await database.CreateDatabaseAsync("bindings_platform", CancellationToken);
        await SeedPlatformAsync(good, new SeededTenant("a", tenantDatabase, "a.example.test", [Storage("Files", "database")]));
        await GrantPlatformAsync(good);

        var catalog = await DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(good), _cipher, CancellationToken);

        var binding = catalog.Find("a")!.Bindings["Files"];
        Assert.Equal((DeploymentBindingKinds.Storage, "database"), (binding.Kind, binding.Url));

        var bad = await database.CreateDatabaseAsync("bindings_platform", CancellationToken);
        await SeedPlatformAsync(bad, new SeededTenant("a", tenantDatabase, "a.example.test", [Storage("Files", "s3")]));
        await GrantPlatformAsync(bad);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DirectoryTenantCatalog.LoadAsync(PostgreSqlServer.RuntimeConnectionString(bad), _cipher, CancellationToken));

        Assert.Contains("Tenant 'a'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Files", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3", failure.Message, StringComparison.Ordinal);
    }

    private static WebApplication BuildWorker(string platform, LogCollector logs)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
            ["Platform:SecretKey"] = TestSecretKey,
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
        });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(logs);

        // Development, so that the worker accepts the stub's http address.
        builder.Services.AddAxisWorker(builder.Configuration, new HostingEnvironment { EnvironmentName = Environments.Development });
        return builder.Build();
    }

    private static TenantBindingRow Url(string name, string url) => new() { Name = name, Kind = DeploymentBindingKinds.Url, Url = url };

    private static TenantBindingRow Storage(string name, string value) => new() { Name = name, Kind = DeploymentBindingKinds.Storage, Url = value };

    private static TenantBindingRow Secret(string tenantId, string name, string value) =>
        new() { Name = name, Kind = DeploymentBindingKinds.Secret, SecretValue = _cipher.Encrypt(tenantId, TenantSecretFields.Binding(name), value) };

    /// <summary>Creates the directory tables with one active tenant row, host and bindings per tenant.</summary>
    private static async Task SeedPlatformAsync(string platform, params SeededTenant[] tenants)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        foreach (var tenant in tenants)
        {
            var builder = new NpgsqlConnectionStringBuilder(tenant.DatabaseConnectionString);
            context.Tenants.Add(new TenantRow
            {
                Id = tenant.Id,
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = builder.Host!,
                DatabasePort = builder.Port,
                DatabaseName = builder.Database!,
                RuntimeUser = PostgreSqlServer.RuntimeRole,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.RuntimePassword, PostgreSqlServer.RolePassword),
                SchemaPassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = "https://127.0.0.1:1/realms/axis",
                OidcClientId = "axis",
                OidcClientSecret = _cipher.Encrypt(tenant.Id, TenantSecretFields.OidcClientSecret, "test"),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = tenant.Host });
            foreach (var binding in tenant.Bindings)
            {
                binding.TenantId = tenant.Id;
                context.TenantBindings.Add(binding);
            }
        }

        await context.SaveChangesAsync(CancellationToken);
    }

    private static async Task GrantPlatformAsync(string platform)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private sealed record SeededTenant(string Id, string DatabaseConnectionString, string Host, TenantBindingRow[] Bindings);
}
