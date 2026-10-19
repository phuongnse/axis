using System.Security.Cryptography;
using Axis.Admin;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

internal sealed record AdminResult(int Exit, string[] Output, string[] Error);

/// <summary>One platform database and its admin roles. It drops the databases of the tenants it made.</summary>
internal sealed class AdminScenario(string platform, bool migrated) : IAsyncDisposable
{
    private readonly string _clientSecret = $"client-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
    private readonly List<string> _adminPasswords = [];
    private readonly List<string> _texts = [];
    private readonly List<string> _tenants = [];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public const string TestSecretKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public const string Authority = "https://127.0.0.1:1/realms/axis";

    public static readonly TenantSecretCipher Cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    public string Platform { get; } = platform;

    /// <summary>The OIDC client secret that <c>tenant create</c> gets, for tests that check it never leaks.</summary>
    public string ClientSecret => _clientSecret;

    /// <summary>The passwords of the admin roles this scenario made.</summary>
    public IReadOnlyList<string> AdminPasswords => _adminPasswords;

    /// <summary>The connection string of the admin role that every command uses unless a test passes another.</summary>
    public string AdminConnection { get; private set; } = "";

    public static string[] CreateArgs(string id, params string[] hosts) =>
    [
        "tenant", "create", id,
        .. hosts.SelectMany(host => new[] { "--host", host }),
        "--oidc-authority", Authority,
        "--oidc-client-id", "axis",
    ];

    /// <summary>Creates a platform database, migrates it unless <paramref name="migrate"/> is false, and returns the scenario on it.</summary>
    public static async Task<AdminScenario> NewAsync(PostgreSqlFixture database, bool migrate, CancellationToken cancellationToken)
    {
        var platform = await database.CreateDatabaseAsync("platform", cancellationToken);
        if (migrate)
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options;
            await using var context = new PlatformDbContext(options);
            await context.Database.MigrateAsync(cancellationToken);
        }

        var scenario = new AdminScenario(platform, migrate);
        await scenario.InitializeAsync();
        return scenario;
    }

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

    public Task<AdminResult> CreateAsync(string id, params string[] hosts) => RunAsync(CreateArgs(id, hosts));

    /// <summary>Runs a command in process and keeps its output, so <see cref="AssertNoSecretsAsync"/> can check it.</summary>
    public async Task<AdminResult> RunAsync(
        string[] args,
        TextWriter? output = null,
        CancellationTokenSource? stop = null,
        string? adminConnectionString = null,
        bool withClientSecret = true,
        string? path = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Admin:ConnectionString"] = adminConnectionString ?? AdminConnection,
            ["ConnectionStrings:PlatformSchema"] = Platform,
            ["Platform:SecretKey"] = TestSecretKey,
            ["AXIS_OIDC_CLIENT_SECRET"] = withClientSecret ? _clientSecret : null,
            ["PATH"] = path,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var error = new StringWriter();
        output ??= new StringWriter();
        var exit = await AdminCommands.RunAsync(args, configuration, output, error, stop?.Token ?? CancellationToken);
        var text = output.ToString()!;
        _texts.Add(text);
        _texts.Add(error.ToString());
        return new AdminResult(exit, Lines(text), Lines(error.ToString()));
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

    /// <summary>Sets the schema version recorded for a tenant, as if its database were on another schema.</summary>
    public async Task SetSchemaVersionAsync(string id, string schemaVersion)
    {
        await using var context = NewContext();
        var row = await context.Tenants.SingleAsync(tenant => tenant.Id == id, CancellationToken);
        row.SchemaVersion = schemaVersion;
        await context.SaveChangesAsync(CancellationToken);
    }

    public async Task SetStatusAsync(string id, string status)
    {
        await using var context = NewContext();
        var row = await context.Tenants.SingleAsync(tenant => tenant.Id == id, CancellationToken);
        row.Status = status;
        await context.SaveChangesAsync(CancellationToken);
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
                    secrets.Add(Cipher.Decrypt(tenant.Id, TenantSecretFields.SchemaPassword, tenant.SchemaPassword).Reveal());
                }

                if (tenant.RuntimePassword.Length > 0)
                {
                    secrets.Add(Cipher.Decrypt(tenant.Id, TenantSecretFields.RuntimePassword, tenant.RuntimePassword).Reveal());
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

/// <summary>A line ends a stop request: the token is canceled once the database step has reported.</summary>
internal sealed class CancelOnLineWriter(CancellationTokenSource source) : StringWriter
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
