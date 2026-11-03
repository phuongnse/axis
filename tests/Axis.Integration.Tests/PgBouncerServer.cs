using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.PgBouncerServer))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real PgBouncer container in transaction mode in front of the PostgreSQL of <see cref="PostgreSqlServer"/>,
/// shared by the whole run, as a cell runs it. It accepts <see cref="PostgreSqlServer.RuntimeRole"/> only. It starts the first time a test needs it,
/// so a run that needs no PgBouncer never starts one. Test databases have unique names, so one wildcard database entry serves them all.
/// The pool sizes are the starting values of the cell connections doc, and named prepared statements are
/// refused, so a code path that relies on session state fails instead of passing. The container is held in static state, as
/// the PostgreSQL container is, and this assembly fixture only removes it when the run ends.
/// </summary>
public sealed class PgBouncerServer : IAsyncDisposable
{
    public const string Image = "edoburu/pgbouncer:v1.24.1-p1";

    private const int ContainerPort = 5432;

    // The start takes no test's cancellation token, so cancelling one test does not fail the others.
    private static readonly Lazy<Task<IContainer>> _container = new(StartContainerAsync);

    /// <summary>
    /// The same database as <paramref name="connectionString"/> as <see cref="PostgreSqlServer.RuntimeRole"/>, reached through PgBouncer.
    /// Starts PgBouncer if no test has yet.
    /// </summary>
    public static async Task<string> RuntimeConnectionStringAsync(string connectionString) =>
        await ThroughAsync(PostgreSqlServer.RuntimeConnectionString(connectionString));

    /// <summary>
    /// <paramref name="connectionString"/> with PgBouncer's host and port in place of its own.
    /// Starts PgBouncer if no test has yet.
    /// </summary>
    public static async Task<string> ThroughAsync(string connectionString)
    {
        var container = await _container.Value;
        return new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(ContainerPort),
        }.ConnectionString;
    }

    /// <summary>The address PgBouncer connects to PostgreSQL from, as <c>pg_stat_activity</c> shows it in <c>client_addr</c>.</summary>
    public static async Task<string> IpAddressAsync() => (await _container.Value).IpAddress;

    /// <summary>
    /// Asserts on the database that <paramref name="connectionString"/> names, as the superuser, that no session of the runtime role
    /// is <c>idle in transaction</c> and that at least one comes from PgBouncer's address, so the check is not empty. With
    /// <paramref name="onlyThroughPgBouncer"/>, every session of the runtime role must come from there, for a test whose only
    /// runtime connections go through PgBouncer.
    /// </summary>
    public static async Task AssertNoRuntimeTransactionLeftOpenAsync(string connectionString, bool onlyThroughPgBouncer)
    {
        var address = await IpAddressAsync();
        var sessions = $"SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND usename = '{PostgreSqlServer.RuntimeRole}'";

        Assert.True(
            (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, $"{sessions} AND host(client_addr) = '{address}'"))! >= 1,
            "The runtime role has no session from PgBouncer in the database, so the check below would pass on nothing.");
        if (onlyThroughPgBouncer)
        {
            Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(connectionString, $"{sessions} AND host(client_addr) IS DISTINCT FROM '{address}'"));
        }

        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(connectionString, $"{sessions} AND state LIKE 'idle in transaction%'"));
    }

    public async ValueTask DisposeAsync()
    {
        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    // The settings under test are written out here and not set by the image's environment variables, so they do not
    // depend on how the image's entrypoint builds its file. The entrypoint leaves a file that exists as it is.
    private static async Task<IContainer> StartContainerAsync()
    {
        var postgres = await PostgreSqlServer.ContainerIpAddressAsync();
        var ini = $"""
            [databases]
            * = host={postgres} port=5432

            [pgbouncer]
            listen_addr = 0.0.0.0
            listen_port = {ContainerPort}
            auth_type = scram-sha-256
            auth_file = /etc/pgbouncer/userlist.txt
            pool_mode = transaction
            default_pool_size = 3
            max_db_connections = 10
            max_client_conn = 10000
            max_prepared_statements = 0
            ignore_startup_parameters = extra_float_digits

            """;
        var userlist = $"\"{PostgreSqlServer.RuntimeRole}\" \"{PostgreSqlServer.RolePassword}\"\n";
        var container = new ContainerBuilder(Image)
            .WithPortBinding(ContainerPort, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(ini), "/etc/pgbouncer/pgbouncer.ini")
            .WithResourceMapping(Encoding.UTF8.GetBytes(userlist), "/etc/pgbouncer/userlist.txt")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("process up"))
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
