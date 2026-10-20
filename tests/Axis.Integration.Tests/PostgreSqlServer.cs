using System.Text.RegularExpressions;
using Axis.Data.Storage;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.PostgreSqlServer))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real PostgreSQL container shared by the whole run. It starts the first time a test needs it,
/// so a run that needs no PostgreSQL never starts one. Fixtures create <see cref="PostgreSqlFixture"/>
/// with <c>new</c>, so xUnit cannot inject a shared instance into them, and the container is held in
/// static state. This assembly fixture only removes the container when the run ends.
/// </summary>
public sealed partial class PostgreSqlServer : IAsyncDisposable
{
    public const string Image = "postgres:18-alpine";

    /// <summary>
    /// xUnit runs about one test class per CPU core in parallel, and one class can hold about 30
    /// connections: the server pools, the worker, data sources and the 20 concurrent requests in
    /// <see cref="RecordSequenceEndpointTests"/>. The default of 100 is too low for that.
    /// </summary>
    public const int MaxConnections = 500;

    /// <summary>The login role that owns every test database and runs startup activation.</summary>
    public const string SchemaRole = "axis_schema";

    /// <summary>The login role that servers and workers connect as. It gets row access only.</summary>
    public const string RuntimeRole = "axis_runtime";

    /// <summary>
    /// The login role that creates and drops the temporary databases of test scenarios. It has <c>CREATEDB</c> and owns no
    /// test database.
    /// </summary>
    public const string ScenarioRole = "axis_scenario";

    /// <summary>The development password of the three roles.</summary>
    public const string RolePassword = "axis";

    // The start takes no test's cancellation token, so cancelling one test does not fail the others.
    private static readonly Lazy<Task<PostgreSqlContainer>> _container = new(StartContainerAsync);

    /// <summary>
    /// Creates a new database named <c>&lt;prefix&gt;_&lt;guid&gt;</c>, unique in the run, owned by
    /// <see cref="SchemaRole"/>, and returns that role's connection string to it. <see cref="RuntimeRole"/>
    /// has no rights in it until a server starts or <see cref="GrantRuntimeRoleAsync"/> runs. The prefix is at most 30 lowercase letters, digits and underscores,
    /// so the name stays within PostgreSQL's 63-character limit.
    /// </summary>
    public static async Task<string> CreateDatabaseAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (!PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException(
                "A database prefix starts with a lowercase letter and has at most 30 lowercase letters, digits and underscores.",
                nameof(prefix));
        }

        var name = $"{prefix}_{Guid.NewGuid():N}";
        var admin = await AdminConnectionStringAsync();
        await ExecuteAsync(admin, $"""CREATE DATABASE "{name}" OWNER {SchemaRole}""", cancellationToken);

        return new NpgsqlConnectionStringBuilder(admin)
        {
            Database = name,
            Username = SchemaRole,
            Password = RolePassword,
        }.ConnectionString;
    }

    /// <summary>
    /// Runs the server's grant step on the database that <paramref name="schemaConnectionString"/> names, for
    /// fixtures that migrate a database themselves. Call it after the migrations.
    /// </summary>
    public static async Task GrantRuntimeRoleAsync(string schemaConnectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(schemaConnectionString);
        await connection.OpenAsync(cancellationToken);
        await RuntimeRoleGrants.ApplyAsync(connection, RuntimeRole, cancellationToken);
    }

    /// <summary>The same database as <paramref name="connectionString"/>, as <see cref="RuntimeRole"/>.</summary>
    public static string RuntimeConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Username = RuntimeRole, Password = RolePassword }.ConnectionString;

    /// <summary>
    /// Runs <paramref name="sql"/> as the container's superuser on the same database as
    /// <paramref name="connectionString"/> and returns the first column of the first row. Only a
    /// superuser sees other roles' sessions in <c>pg_stat_activity</c> or ends them. The session does not
    /// outlive the call, so it does not show up in later checks.
    /// </summary>
    public static async Task<object?> AdminScalarAsync(string connectionString, string sql, CancellationToken cancellationToken = default)
    {
        var admin = new NpgsqlConnectionStringBuilder(await AdminConnectionStringAsync())
        {
            Database = new NpgsqlConnectionStringBuilder(connectionString).Database,
            Pooling = false,
        }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(admin);
        await using var command = dataSource.CreateCommand(sql);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    /// <summary>The connection string of <see cref="ScenarioRole"/> to the maintenance database, as <c>Scenarios:ServerConnectionString</c> takes it.</summary>
    public static async Task<string> ScenarioConnectionStringAsync() =>
        new NpgsqlConnectionStringBuilder(await AdminConnectionStringAsync())
        {
            Database = "postgres",
            Username = ScenarioRole,
            Password = RolePassword,
        }.ConnectionString;

    /// <summary>Drops the database and ends every session still connected to it.</summary>
    public static async Task DropDatabaseAsync(string name, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(await AdminConnectionStringAsync(), $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    /// <summary>The id of the container, for tests that run the PostgreSQL client tools inside it with <c>docker exec</c>.</summary>
    public static async Task<string> ContainerIdAsync() => (await _container.Value).Id;

    /// <summary>The connection string to the maintenance database, which creates and drops the others.</summary>
    public static async Task<string> AdminConnectionStringAsync() => (await _container.Value).GetConnectionString();

    // CREATE DATABASE and DROP DATABASE cannot run in a transaction block, so each is its own command.
    private static async Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PostgreSqlContainer> StartContainerAsync()
    {
        var container = new PostgreSqlBuilder(Image)
            .WithCommand("-c", $"max_connections={MaxConnections}")
            .Build();
        try
        {
            await container.StartAsync();
            await ExecuteAsync(
                container.GetConnectionString(),
                $"CREATE ROLE {SchemaRole} LOGIN PASSWORD '{RolePassword}'; CREATE ROLE {RuntimeRole} LOGIN PASSWORD '{RolePassword}'; CREATE ROLE {ScenarioRole} LOGIN PASSWORD '{RolePassword}' CREATEDB",
                CancellationToken.None);
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,29}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
