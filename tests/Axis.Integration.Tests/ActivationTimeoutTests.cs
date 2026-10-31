using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ActivationTimeoutTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application and entity ids and names, so tests sharing the database do
    // not see each other's tables and active releases.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";

    private static readonly TimeSpan _limit = TimeSpan.FromMilliseconds(200);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_activation_that_waits_on_a_locked_table_past_the_limit_rolls_back_and_keeps_the_previous_release_active()
    {
        using var folder = ApplicationFolder(withNote: false);
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first, database.ConnectionString, ReleaseActivator.DefaultActivationTimeout, CancellationToken)).Diagnostics);
        folder.With("entities/order.json", OrderFile(withNote: true));
        var second = await CompileAsync(folder);

        await TimeOutAsync(second, database.ConnectionString);

        await using (var context = database.CreateConfigurationContext())
        {
            var active = await new ActiveReleaseStore(context).FindByApplicationIdAsync(_applicationId, CancellationToken);
            Assert.Equal(first.Release?.Id, active?.ReleaseId);
        }

        Assert.Equal(0L, await CountNoteColumnsAsync());
    }

    [Fact]
    public async Task The_next_user_of_the_pooled_connection_of_a_timed_out_activation_has_no_limit()
    {
        using var folder = ApplicationFolder(withNote: false);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder), database.ConnectionString, ReleaseActivator.DefaultActivationTimeout, CancellationToken)).Diagnostics);
        folder.With("entities/order.json", OrderFile(withNote: true));
        var second = await CompileAsync(folder);

        // The pool holds one connection, which No Reset On Close would hand on with a plain SET still in force.
        var pooled = database.ConnectionString + ";Maximum Pool Size=1;No Reset On Close=true";
        await TimeOutAsync(second, pooled);

        await using var connection = new NpgsqlConnection(pooled);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SHOW statement_timeout", connection);
        Assert.Equal("0", (string?)await command.ExecuteScalarAsync(CancellationToken));
    }

    // Holds a lock on the order table while the release is activated under the limit, and expects the limit to end it.
    private async Task TimeOutAsync(ReleaseCompilationResult compiled, string connectionString)
    {
        await using var locker = new NpgsqlConnection(database.ConnectionString);
        await locker.OpenAsync(CancellationToken);
        await using var transaction = await locker.BeginTransactionAsync(CancellationToken);
        await using (var command = new NpgsqlCommand($"LOCK TABLE {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))} IN EXCLUSIVE MODE", locker, transaction))
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        // A stuck activation fails the test instead of hanging it.
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(30));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ActivateAsync(compiled, connectionString, _limit, guard.Token));

        Assert.Equal("57014", failure.SqlState);
        await transaction.RollbackAsync(CancellationToken);
    }

    private async Task<long> CountNoteColumnsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'entities' AND table_name = @table AND column_name = 'f_note'");
        command.Parameters.AddWithValue("table", EntityNaming.Table(_orderId));
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private string OrderFile(bool withNote) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
          "displayField": "title",
          "fields": [
            { "name": "title", "type": "text", "required": true }{{(withNote ? ", { \"name\": \"note\", \"type\": \"text\" }" : "")}}
          ] }
        """;

    private TemporaryFolder ApplicationFolder(bool withNote) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/order.json", OrderFile(withNote));

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    private static async Task<ActivationResult> ActivateAsync(
        ReleaseCompilationResult compiled, string connectionString, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(
            compiled,
            new ActiveReleaseStore(configuration),
            data,
            ActivationBindings.None("default"),
            OpenInstanceCounter.Instance,
            ReleaseActivator.DefaultInlineRecomputeLimit,
            timeout,
            ActivationActor.Startup,
            cancellationToken);
    }
}
