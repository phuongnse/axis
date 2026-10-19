using System.Net;
using System.Security.Cryptography;
using Axis.Admin;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminTenantCreateTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string TestSecretKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string Authority = "https://127.0.0.1:1/realms/axis";

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_makes_a_tenant_that_the_server_serves_and_whose_runtime_role_cannot_create_tables()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();

        var result = await scenario.CreateAsync(id, "Shop.Example.Test");

        Assert.Equal(0, result.Exit);
        Assert.Equal(
            ["entry", "roles", "database", "grants", "migrations", "grants", "role-check", "version"],
            result.Output.Select(line => line.Split(' ')[1]));
        Assert.All(result.Output, line => Assert.EndsWith(" ok", line, StringComparison.Ordinal));
        var row = await scenario.RowAsync(id);
        Assert.Equal("active", row.Status);
        Assert.NotEmpty(row.SchemaVersion);
        Assert.Equal($"axis_{id}", row.DatabaseName);

        await using (var factory = CreateFactory(scenario.Platform))
        {
            using var client = factory.CreateClient().SignedInAs("anna");
            using var served = await client.SendAsync(Request("/api/me", "shop.example.test"), CancellationToken);
            using var unknown = await client.SendAsync(Request("/api/me", "other.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        var runtime = _cipher.Decrypt(id, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(id, row.RuntimeUser, runtime));
        await connection.OpenAsync(CancellationToken);
        await using var create = new NpgsqlCommand("CREATE TABLE entities.x(i int)", connection);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync(CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);

        // Another login role of the cluster cannot connect to the tenant database.
        await using var other = new NpgsqlConnection(
            scenario.TenantConnectionString(id, PostgreSqlServer.RuntimeRole, PostgreSqlServer.RolePassword));
        var refused = await Assert.ThrowsAsync<PostgresException>(() => other.OpenAsync(CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_create_stopped_after_the_database_exists_stays_provisioning_and_a_rerun_finishes_it_with_the_same_passwords()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        var output = new CancelOnLineWriter(stop);

        var first = await scenario.RunAsync(Scenario.CreateArgs(id, "shop.example.test"), output, stop);

        Assert.Equal(1, first.Exit);
        Assert.Contains($"{id} database ok", first.Output);
        Assert.Contains($"{id} grants failed: canceled", first.Output);
        var stopped = await scenario.RowAsync(id);
        Assert.Equal("provisioning", stopped.Status);
        Assert.True(await scenario.DatabaseExistsAsync(id));

        var second = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(0, second.Exit);
        var finished = await scenario.RowAsync(id);
        Assert.Equal("active", finished.Status);
        Assert.NotEmpty(finished.SchemaVersion);
        Assert.Equal(stopped.SchemaPassword, finished.SchemaPassword);
        Assert.Equal(stopped.RuntimePassword, finished.RuntimePassword);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_rerun_on_a_provisioning_entry_updates_its_hosts_and_never_its_passwords()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        await scenario.RunAsync(Scenario.CreateArgs(id, "shop.exampel.test"), new CancelOnLineWriter(stop), stop);
        var stopped = await scenario.RowAsync(id);

        var rerun = await scenario.CreateAsync(id, "SHOP.example.test");

        Assert.Equal(0, rerun.Exit);
        Assert.Equal(["SHOP.example.test"], await scenario.HostsAsync(id));
        var finished = await scenario.RowAsync(id);
        Assert.Equal(stopped.SchemaPassword, finished.SchemaPassword);
        Assert.Equal(stopped.RuntimePassword, finished.RuntimePassword);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_for_an_active_tenant_exits_1_and_changes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "shop.example.test")).Exit);
        var before = await scenario.SnapshotAsync();

        var again = await scenario.CreateAsync(id, "other.example.test");

        Assert.Equal(1, again.Exit);
        Assert.Equal([$"{id} entry failed: the tenant already exists"], again.Output);
        Assert.Equal(before, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Theory]
    [InlineData("shop.example.test")]
    [InlineData("SHOP.Example.TEST")]
    public async Task Create_with_a_host_of_another_tenant_exits_2_and_creates_nothing(string conflicting)
    {
        await using var scenario = await NewScenarioAsync();
        var first = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(first, "shop.example.test")).Exit);
        var before = await scenario.SnapshotAsync();
        var second = scenario.NewTenantId();

        var result = await scenario.CreateAsync(second, "free.example.test", conflicting);

        Assert.Equal(2, result.Exit);
        Assert.Equal([$"{second} entry failed: a host belongs to another tenant"], result.Output);
        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.False(await scenario.DatabaseExistsAsync(second));
        Assert.Equal(0L, await scenario.CountRolesAsync(second));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_with_a_superuser_admin_role_exits_2_and_writes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var superuser = await PostgreSqlServer.AdminConnectionStringAsync();

        var result = await scenario.RunAsync(Scenario.CreateArgs(id, "shop.example.test"), adminConnectionString: superuser);

        Assert.Equal(2, result.Exit);
        Assert.Contains("superuser", result.Error[0], StringComparison.Ordinal);
        Assert.Empty(result.Output);
        Assert.Equal(0L, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0L, await scenario.CountRolesAsync(id));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_with_an_admin_role_that_lacks_CREATEROLE_exits_2_and_writes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var limited = await scenario.NewAdminRoleAsync("CREATEDB");

        var result = await scenario.RunAsync(Scenario.CreateArgs(id, "shop.example.test"), adminConnectionString: limited);

        Assert.Equal(2, result.Exit);
        Assert.Contains("CREATEDB and CREATEROLE", result.Error[0], StringComparison.Ordinal);
        Assert.Equal(0L, await scenario.CountEntriesAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_before_the_platform_tables_exist_exits_2()
    {
        await using var scenario = await NewScenarioAsync(migrate: false);
        var id = scenario.NewTenantId();

        var result = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(2, result.Exit);
        Assert.Contains("platform tables do not exist", result.Error[0], StringComparison.Ordinal);
        Assert.False(await scenario.DatabaseExistsAsync(id));

        await scenario.AssertNoSecretsAsync();
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("under_score")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Create_with_an_invalid_id_exits_2(string id)
    {
        await using var scenario = await NewScenarioAsync();

        var result = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(2, result.Exit);
        Assert.Contains("tenant id", result.Error[0], StringComparison.Ordinal);
        Assert.Equal(0L, await scenario.CountEntriesAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_without_the_client_secret_or_with_a_wrong_shape_exits_2()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();

        var noSecret = await scenario.RunAsync(Scenario.CreateArgs(id, "shop.example.test"), withClientSecret: false);
        var noHost = await scenario.RunAsync(["tenant", "create", id, "--oidc-authority", Authority, "--oidc-client-id", "axis"]);
        var unknown = await scenario.RunAsync(["tenant", "delete", id]);

        Assert.Equal(2, noSecret.Exit);
        Assert.Contains("AXIS_OIDC_CLIENT_SECRET", noSecret.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, noHost.Exit);
        Assert.StartsWith("Usage:", noHost.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, unknown.Exit);
        Assert.Equal(0L, await scenario.CountEntriesAsync());

        await scenario.AssertNoSecretsAsync();
    }

    private static WebApplicationFactory<Program> CreateFactory(string platform) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(platform));
            builder.UseSetting("ConnectionStrings:PlatformSchema", platform);
            builder.UseSetting("Platform:SecretKey", TestSecretKey);
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna Employee", "employee"));
        });

    private async Task<Scenario> NewScenarioAsync(bool migrate = true)
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        if (migrate)
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options;
            await using var context = new PlatformDbContext(options);
            await context.Database.MigrateAsync(CancellationToken);
        }

        var scenario = new Scenario(platform, migrate);
        await scenario.InitializeAsync();
        return scenario;
    }

    /// <summary>A line ends a stop request: the token is canceled once the database step has reported.</summary>
    private sealed class CancelOnLineWriter(CancellationTokenSource source) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value is not null && value.EndsWith(" database ok", StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }

    private sealed record Result(int Exit, string[] Output, string[] Error);

    /// <summary>One platform database and its admin roles. It drops the databases of the tenants it made.</summary>
    private sealed class Scenario(string platform, bool migrated) : IAsyncDisposable
    {
        private readonly string _clientSecret = $"client-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
        private readonly List<string> _adminPasswords = [];
        private readonly List<string> _texts = [];
        private readonly List<string> _tenants = [];

        public string Platform { get; } = platform;

        /// <summary>The connection string of the admin role that every command uses unless a test passes another.</summary>
        public string AdminConnection { get; private set; } = "";

        public static string[] CreateArgs(string id, params string[] hosts) =>
        [
            "tenant", "create", id,
            .. hosts.SelectMany(host => new[] { "--host", host }),
            "--oidc-authority", Authority,
            "--oidc-client-id", "axis",
        ];

        public async Task InitializeAsync() => AdminConnection = await NewAdminRoleAsync("CREATEDB CREATEROLE");

        public string NewTenantId()
        {
            var id = $"t-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";
            _tenants.Add(id);
            return id;
        }

        /// <summary>Creates a login role with the given attributes and returns its connection string.</summary>
        public async Task<string> NewAdminRoleAsync(string attributes)
        {
            var name = $"adm_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";
            var password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
            await PostgreSqlServer.AdminScalarAsync(Platform, $"CREATE ROLE {name} LOGIN PASSWORD '{password}' {attributes}", CancellationToken);
            _adminPasswords.Add(password);
            return new NpgsqlConnectionStringBuilder(await PostgreSqlServer.AdminConnectionStringAsync())
            {
                Username = name,
                Password = password,
            }.ConnectionString;
        }

        public Task<Result> CreateAsync(string id, params string[] hosts) => RunAsync(CreateArgs(id, hosts));

        /// <summary>Runs a command in process and keeps its output, so <see cref="AssertNoSecretsAsync"/> can check it.</summary>
        public async Task<Result> RunAsync(
            string[] args,
            TextWriter? output = null,
            CancellationTokenSource? stop = null,
            string? adminConnectionString = null,
            bool withClientSecret = true)
        {
            var settings = new Dictionary<string, string?>
            {
                ["Admin:ConnectionString"] = adminConnectionString ?? AdminConnection,
                ["ConnectionStrings:PlatformSchema"] = Platform,
                ["Platform:SecretKey"] = TestSecretKey,
                ["AXIS_OIDC_CLIENT_SECRET"] = withClientSecret ? _clientSecret : null,
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            using var error = new StringWriter();
            output ??= new StringWriter();
            var exit = await AdminCommands.RunAsync(args, configuration, output, error, stop?.Token ?? CancellationToken);
            var text = output.ToString()!;
            _texts.Add(text);
            _texts.Add(error.ToString());
            return new Result(exit, Lines(text), Lines(error.ToString()));
        }

        public string TenantConnectionString(string id, string user, string password) =>
            new NpgsqlConnectionStringBuilder(Platform) { Database = $"axis_{id}", Username = user, Password = password }.ConnectionString;

        public async Task<TenantRow> RowAsync(string id)
        {
            await using var context = NewContext();
            return await context.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == id, CancellationToken);
        }

        public async Task<string[]> HostsAsync(string id)
        {
            await using var context = NewContext();
            return await context.TenantHosts.AsNoTracking().Where(host => host.TenantId == id).Select(host => host.Host).ToArrayAsync(CancellationToken);
        }

        public async Task<long> CountEntriesAsync()
        {
            await using var context = NewContext();
            return await context.Tenants.LongCountAsync(CancellationToken);
        }

        public async Task<bool> DatabaseExistsAsync(string id) =>
            (bool)(await PostgreSqlServer.AdminScalarAsync(Platform, $"SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'axis_{id}')", CancellationToken))!;

        public async Task<long> CountRolesAsync(string id) =>
            (long)(await PostgreSqlServer.AdminScalarAsync(Platform, $"SELECT count(*) FROM pg_roles WHERE starts_with(rolname, 'axis_{id}_')", CancellationToken))!;

        /// <summary>Every column of every tenant and host row, in a stable order.</summary>
        public async Task<string> SnapshotAsync()
        {
            await using var context = NewContext();
            var tenants = await context.Tenants.AsNoTracking().OrderBy(tenant => tenant.Id).ToListAsync(CancellationToken);
            var hosts = await context.TenantHosts.AsNoTracking().OrderBy(host => host.Host).ToListAsync(CancellationToken);
            return string.Join(
                '\n',
                tenants.Select(t => string.Join(
                    '|',
                    t.Id, t.Status, t.Version, t.DatabaseHost, t.DatabasePort, t.DatabaseName, t.RuntimeUser, t.SchemaUser,
                    Convert.ToHexString(t.RuntimePassword), Convert.ToHexString(t.SchemaPassword), t.OidcAuthority, t.OidcClientId,
                    Convert.ToHexString(t.OidcClientSecret), t.SchemaVersion))
                .Concat(hosts.Select(host => $"{host.TenantId}|{host.Host}")));
        }

        /// <summary>
        /// Checks that no output of any command held the admin password, a generated password, the client secret or a
        /// connection string with a password.
        /// </summary>
        public async Task AssertNoSecretsAsync()
        {
            var secrets = new List<string>([.. _adminPasswords, _clientSecret]);
            await using (var context = NewContext())
            {
                // Without the platform tables there is no directory, so no generated password.
                foreach (var tenant in migrated ? await context.Tenants.AsNoTracking().ToListAsync(CancellationToken) : [])
                {
                    if (tenant.SchemaPassword.Length > 0)
                    {
                        secrets.Add(_cipher.Decrypt(tenant.Id, TenantSecretFields.SchemaPassword, tenant.SchemaPassword).Reveal());
                    }

                    if (tenant.RuntimePassword.Length > 0)
                    {
                        secrets.Add(_cipher.Decrypt(tenant.Id, TenantSecretFields.RuntimePassword, tenant.RuntimePassword).Reveal());
                    }
                }
            }

            Assert.All(_texts, text =>
            {
                Assert.All(secrets, secret => Assert.DoesNotContain(secret, text, StringComparison.Ordinal));
                Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
            });
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var id in _tenants)
            {
                await PostgreSqlServer.DropDatabaseAsync($"axis_{id}");
            }
        }

        private PlatformDbContext NewContext() => new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(Platform).Options);

        private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
