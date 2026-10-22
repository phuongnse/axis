using System.Diagnostics;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The check for open work on older releases answers within the time limit with 20 releases and
/// ten thousand waiting instances among a hundred thousand, and its query reads an index.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class OldReleaseCheckPerformanceTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const int Releases = 20;
    private const int Instances = 100_000;
    private const int WaitingInstances = 10_000;

    // The check counts the open instances with one query and then reads and parses the 20 stored
    // releases. It took under a tenth of a second when measured. The bound is far above that, so
    // shared CI runners do not make the test flaky. It still catches a check that runs one query
    // per instance, which would take minutes. The plan test below catches a lost index.
    private static readonly TimeSpan _checkTime = TimeSpan.FromSeconds(10);

    private const string NumberField = """{ "name": "number", "type": "text", "required": true, "maxLength": 20 }""";
    private const string NoteField = """{ "name": "note", "type": "text" }""";

    // Each test uses its own application and entity ids and name, so the releases and the active
    // release of one test do not reach another.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activating_a_drop_field_with_20_releases_in_use_is_refused_for_each_of_them_within_the_time_limit()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        var releases = await CompileReleasesAsync(folder);
        await InsertInstancesAsync(releases);
        var migrationId = Guid.NewGuid();
        folder
            .With("entities/order.json", Order(NumberField))
            .With("migrations/0001-drop-note.json", $$"""
                { "id": "{{migrationId}}", "kind": "migration", "name": "DropNote", "formatVersion": 1, "entity": "Order", "changes": [ { "type": "dropField", "field": "note" } ] }
                """);
        var dropping = await CompileAsync(folder);

        var stopwatch = Stopwatch.StartNew();
        var result = await ActivateAsync(dropping);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The check of {Releases} releases took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _checkTime, $"The check of {Releases} releases took {stopwatch.Elapsed}.");
        Assert.Equal(Releases, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.OldReleaseWorkOpen, diagnostic.Code));
        foreach (var release in releases)
        {
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message.Contains($"'{release}'", StringComparison.Ordinal));
        }

        Assert.Equal(first.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task The_open_work_count_over_a_hundred_thousand_instances_reads_an_index()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertInstancesAsync(await CompileReleasesAsync(folder));
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);

        var plan = await QueryPlans.ExplainAsync(
            dataSource,
            OpenInstanceCounter.CountByReleaseSql,
            command =>
            {
                command.Parameters.AddWithValue("application", _applicationId);
                command.Parameters.AddWithValue("running", ProcessStarts.Running);
                command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
            },
            CancellationToken);

        QueryPlans.AssertUsesIndex(plan, ProcessStarts.ActiveSubjectIndex);
    }

    private TemporaryFolder Application() =>
        new TemporaryFolder().With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""");

    private string Order(params string[] fields) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    // Stores 20 releases that differ in the length limit of the number, and activates none of them.
    // Every one keeps the note field.
    private async Task<Guid[]> CompileReleasesAsync(TemporaryFolder folder)
    {
        var releases = new Guid[Releases];
        for (var i = 0; i < Releases; i++)
        {
            folder.With("entities/order.json", Order($$"""{ "name": "number", "type": "text", "required": true, "maxLength": {{20 + i + 1}} }""", NoteField));
            releases[i] = (await CompileAsync(folder)).Release!.Id;
        }

        return releases;
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
    }

    // The 10,000 waiting instances are spread evenly over the releases, 500 each. The others have
    // completed, as most instances have in a running system. The subjects are all different, since
    // the unique index on a subject covers open instances only.
    private async Task InsertInstancesAsync(Guid[] releases)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);

        // The other test of this class leaves its instances behind in the same database. Clearing them
        // makes both tests see exactly 100,000 instances.
        await using (var clear = dataSource.CreateCommand("TRUNCATE axis.process_instances CASCADE"))
        {
            await clear.ExecuteNonQueryAsync(CancellationToken);
        }

        await using (var insert = dataSource.CreateCommand(
            $"""
            INSERT INTO axis.process_instances
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
            SELECT gen_random_uuid(), @application, @process, @entity, gen_random_uuid(), (@releases::uuid[])[n % {Releases} + 1],
                   CASE WHEN n <= {WaitingInstances} THEN '{ProcessStarts.Waiting}' ELSE '{ProcessStarts.Completed}' END, 1, 'approve'
            FROM generate_series(1, {Instances}) AS n
            """))
        {
            insert.Parameters.AddWithValue("application", _applicationId);
            insert.Parameters.AddWithValue("process", Guid.NewGuid());
            insert.Parameters.AddWithValue("entity", _orderId);
            insert.Parameters.AddWithValue("releases", releases);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var analyze = dataSource.CreateCommand("ANALYZE axis.process_instances");
        await analyze.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<Guid?> ReadActiveReleaseIdAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT release_id FROM axis.active_releases WHERE application_id = @id");
        command.Parameters.AddWithValue("id", _applicationId);
        return await command.ExecuteScalarAsync(CancellationToken) as Guid?;
    }
}
