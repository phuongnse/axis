using System.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The instance list of a process with a hundred thousand instances, unfiltered, by state and by subject, and
/// the detail of an instance with fifty history rows and twenty call attempts, answer within the time limit.
/// The plan of each page query uses an index and never reads the whole table.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class ProcessInspectionPerformanceTests(ProcessInspectionData data) : IClassFixture<ProcessInspectionData>
{
    private const int PageSize = 50;

    // The count of a list reads every matching instance and checks its subject record, so the unfiltered list
    // is expected to take about 100 ms. A page, a filtered list and a detail take a few milliseconds. The bound
    // is about twenty times the expected time of the slowest list, so shared CI runners do not make the test
    // flaky, and it still catches a slowdown of an order of magnitude. The plan check, not the time, is what
    // catches a lost index.
    private static readonly TimeSpan _listTime = TimeSpan.FromSeconds(2);

    // The detail is four reads by an indexed instance id, which are expected to take a few milliseconds
    // together. The bound is far above that for the same reason, and it still fails when a read scans a table.
    private static readonly TimeSpan _detailTime = TimeSpan.FromMilliseconds(500);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_unfiltered_instance_list_over_a_hundred_thousand_instances_answers_within_the_time_limit_using_the_start_order_index()
    {
        var (page, elapsed) = await ListAsync(state: null, subjectId: null);

        TestContext.Current.TestOutputHelper?.WriteLine($"The unfiltered list took {elapsed}.");
        Assert.True(elapsed < _listTime, $"The unfiltered list took {elapsed}.");
        Assert.Equal(ProcessInspectionData.Instances, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);
        QueryPlans.AssertUsesIndex(await ExplainPageAsync(state: null, subjectId: null), "ix_process_instances_application_id_process_id_created_at_id");
    }

    [Fact]
    public async Task The_instance_list_filtered_by_state_answers_within_the_time_limit_using_the_state_index()
    {
        var (page, elapsed) = await ListAsync(ProcessStarts.Failed, subjectId: null);

        TestContext.Current.TestOutputHelper?.WriteLine($"The list by state took {elapsed}.");
        Assert.True(elapsed < _listTime, $"The list by state took {elapsed}.");
        Assert.Equal(ProcessInspectionData.FailedInstances, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);
        QueryPlans.AssertUsesIndex(await ExplainPageAsync(ProcessStarts.Failed, subjectId: null), "ix_process_instances_application_id_process_id_state_created_at");
    }

    [Fact]
    public async Task The_instance_list_filtered_by_subject_answers_within_the_time_limit_using_the_subject_index()
    {
        var (page, elapsed) = await ListAsync(state: null, data.RepeatedSubjectId);

        TestContext.Current.TestOutputHelper?.WriteLine($"The list by subject took {elapsed}.");
        Assert.True(elapsed < _listTime, $"The list by subject took {elapsed}.");
        Assert.Equal(ProcessInspectionData.RepeatedSubjectInstances, page.TotalCount);
        Assert.Equal(ProcessInspectionData.RepeatedSubjectInstances, page.Items.Count);
        QueryPlans.AssertUsesIndex(await ExplainPageAsync(state: null, data.RepeatedSubjectId), "ix_process_instances_subject_id");
    }

    [Fact]
    public async Task The_detail_of_an_instance_with_fifty_history_rows_and_twenty_call_attempts_answers_within_the_time_limit()
    {
        await using var connection = await data.Steps.DataSource.OpenConnectionAsync(CancellationToken);
        await ReadDetailAsync(connection);

        var stopwatch = Stopwatch.StartNew();
        var (instance, history, tasks, calls) = await ReadDetailAsync(connection);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The detail took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _detailTime, $"The detail took {stopwatch.Elapsed}.");
        Assert.NotNull(instance);
        Assert.Equal(ProcessInspectionData.HistoryRows, history.Count);
        Assert.Empty(tasks);
        Assert.Equal(ProcessInspectionData.AttemptRows, calls.Sum(call => call.Attempts.Count));
    }

    private async Task<(InstanceListPage Page, TimeSpan Elapsed)> ListAsync(string? state, Guid? subjectId)
    {
        await using var connection = await data.Steps.DataSource.OpenConnectionAsync(CancellationToken);
        await InstanceQueries.ListAsync(connection, data.ApplicationId, data.ProcessId, data.Subject, state, subjectId, null, 1, PageSize, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await InstanceQueries.ListAsync(connection, data.ApplicationId, data.ProcessId, data.Subject, state, subjectId, null, 1, PageSize, CancellationToken);
        stopwatch.Stop();
        return (page!, stopwatch.Elapsed);
    }

    private Task<string> ExplainPageAsync(string? state, Guid? subjectId) =>
        QueryPlans.ExplainAsync(
            data.Steps.DataSource,
            InstanceQueries.ListPageSql(data.Subject, null),
            command =>
            {
                InstanceQueries.AddListParameters(command, data.ApplicationId, data.ProcessId, data.Subject.Id, state, subjectId, null);
                command.Parameters.AddWithValue("limit", PageSize);
                command.Parameters.AddWithValue("offset", 0L);
            },
            CancellationToken);

    private async Task<(ProcessInstanceRow? Instance, IReadOnlyList<ProcessStepHistoryRow> History, IReadOnlyList<ProcessTaskRow> Tasks, IReadOnlyList<ProcessCallDetail> Calls)> ReadDetailAsync(NpgsqlConnection connection)
    {
        var instance = await InstanceQueries.FindAsync(connection, data.ApplicationId, data.DetailProcessId, data.Subject, data.DetailInstanceId, CancellationToken);
        var history = await InstanceQueries.HistoryAsync(connection, data.DetailInstanceId, CancellationToken);
        var tasks = await TaskQueries.ListByInstanceAsync(connection, data.DetailInstanceId, CancellationToken);
        var calls = await CallQueries.ListByInstanceAsync(connection, data.DetailInstanceId, CancellationToken);
        return (instance, history, tasks, calls);
    }
}

/// <summary>
/// The StepApp tenant database with the data of <see cref="ProcessInspectionPerformanceTests"/>, which its tests
/// share: a hundred thousand instances of the <c>Review</c> process, and one instance of another process with a
/// long history and many call attempts.
/// </summary>
public sealed class ProcessInspectionData : IAsyncLifetime
{
    /// <summary>The instances of the listed process.</summary>
    public const int Instances = 100_000;

    /// <summary>The instances of the listed process that failed.</summary>
    public const int FailedInstances = 1_000;

    /// <summary>The instances of the one subject that has several.</summary>
    public const int RepeatedSubjectInstances = 4;

    /// <summary>The step history rows of the detail instance.</summary>
    public const int HistoryRows = 50;

    /// <summary>The attempts of the detail instance's calls, over both calls.</summary>
    public const int AttemptRows = 20;

    // The instances are numbered from 1 to the base count. The subject numbered 55 gets three more, which makes the total.
    private const int BaseInstances = Instances - (RepeatedSubjectInstances - 1);
    private const int RepeatedSubjectNumber = 55;

    public ProcessStepFixture Steps { get; } = new("inspection");

    public Guid ApplicationId { get; private set; }

    /// <summary>The process with a hundred thousand instances.</summary>
    public Guid ProcessId { get; private set; }

    /// <summary>The process of the detail instance, which the list does not count.</summary>
    public Guid DetailProcessId { get; } = Guid.NewGuid();

    public Guid DetailInstanceId { get; } = Guid.NewGuid();

    public Guid RepeatedSubjectId { get; private set; }

    /// <summary>The entity of the subject records, <c>Request</c>.</summary>
    public EntityModel Subject { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Steps.InitializeAsync();
        var model = Steps.Model;
        Assert.True(model.TryGetProcess("Review", out var process));
        Assert.True(model.TryGetEntity("Request", out var request));
        ApplicationId = model.Manifest.Id;
        ProcessId = process.Id;
        Subject = request;
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(request.Id));
        var id = EntityNaming.Quote(EntityNaming.IdColumn);

        await Steps.InsertRequestsAsync(Instances, 50m, 1);

        // 90% of the instances completed, 9% wait and 1% failed. Each subject has one, so at most one waits.
        // They started one second apart, the newest first.
        await ExecuteAsync(
            $"""
            INSERT INTO axis.process_instances
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, created_at, ended_at)
            SELECT gen_random_uuid(), @application, @process, @entity, r.id, @release,
                   CASE WHEN r.n % 100 = 1 THEN '{ProcessStarts.Failed}' WHEN r.n % 100 BETWEEN 2 AND 10 THEN '{ProcessStarts.Waiting}' ELSE '{ProcessStarts.Completed}' END,
                   2, 'check', now() - r.n * interval '1 second',
                   CASE WHEN r.n % 100 BETWEEN 2 AND 10 THEN NULL ELSE now() - r.n * interval '1 second' END
            FROM (SELECT {id} AS id, row_number() OVER (ORDER BY {id}) AS n FROM {table}) AS r
            WHERE r.n <= {BaseInstances}
            """);
        RepeatedSubjectId = (Guid)(await ScalarAsync(
            $"SELECT subject_id FROM axis.process_instances WHERE process_id = @process ORDER BY created_at DESC OFFSET {RepeatedSubjectNumber - 1} LIMIT 1"))!;
        await ExecuteAsync(
            $"""
            INSERT INTO axis.process_instances
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, created_at, ended_at)
            SELECT gen_random_uuid(), @application, @process, @entity, @subject, @release, '{ProcessStarts.Completed}', 2, 'check',
                   now() - n * interval '1 hour', now() - n * interval '1 hour'
            FROM generate_series(1, {RepeatedSubjectInstances - 1}) AS n
            """,
            ("subject", RepeatedSubjectId));

        // Every instance has two history rows, so a lookup by instance must use the index.
        await ExecuteAsync(
            """
            INSERT INTO axis.process_step_history (id, process_instance_id, step, revision, input, started_at, finished_at)
            SELECT gen_random_uuid(), i.id, CASE WHEN s = 1 THEN 'check' ELSE 'skipped' END, s, jsonb_build_object(), i.created_at, i.created_at
            FROM axis.process_instances AS i CROSS JOIN generate_series(1, 2) AS s
            WHERE i.process_id = @process
            """);

        // The detail instance is of another process, so the list does not count it. It has two calls with ten attempts each.
        var detailSubject = await Steps.InsertRequestAsync(50m, 1);
        await ExecuteAsync(
            $"""
            INSERT INTO axis.process_instances
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
            VALUES (@detail, @application, @detailProcess, @entity, @detailSubject, @release, '{ProcessStarts.Completed}', {HistoryRows}, 'done');
            INSERT INTO axis.process_step_history (id, process_instance_id, step, revision, input, started_at, finished_at)
            SELECT gen_random_uuid(), @detail, 'step-' || s, s, jsonb_build_object(), now() + s * interval '1 second', now() + s * interval '1 second'
            FROM generate_series(1, {HistoryRows}) AS s;
            INSERT INTO axis.connector_calls (id, process_instance_id, step, staged_revision, release_id, connector, operation, state, attempts)
            SELECT gen_random_uuid(), @detail, 'call-' || s, 1, @release, 'Ledger', 'post', 'succeeded', {AttemptRows / 2}
            FROM generate_series(1, 2) AS s;
            INSERT INTO axis.process_call_attempts (id, call_kind, call_id, kind, number, started_at, finished_at, outcome)
            SELECT gen_random_uuid(), 'connector', c.id, '{CallAttempts.Attempt}', a, now() + a * interval '1 second', now() + a * interval '1 second', 'succeeded'
            FROM axis.connector_calls AS c CROSS JOIN generate_series(1, {AttemptRows / 2}) AS a
            WHERE c.process_instance_id = @detail
            """,
            ("detailSubject", detailSubject));

        foreach (var analyzed in new[] { "axis.process_instances", "axis.process_step_history", "axis.connector_calls", "axis.process_call_attempts", table })
        {
            await ExecuteAsync($"ANALYZE {analyzed}");
        }
    }

    public ValueTask DisposeAsync() => Steps.DisposeAsync();

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Bind(sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var command = Bind(sql, []);
        return await command.ExecuteScalarAsync();
    }

    // Binds the parameters that the setup statements share, and the given ones, when the statement mentions them.
    private NpgsqlCommand Bind(string sql, (string Name, object Value)[] parameters)
    {
        var command = Steps.DataSource.CreateCommand(sql);
        var known = new (string Name, object Value)[]
        {
            ("application", ApplicationId),
            ("process", ProcessId),
            ("entity", Subject.Id),
            ("release", Steps.FirstReleaseId),
            ("detail", DetailInstanceId),
            ("detailProcess", DetailProcessId),
        };
        foreach (var (name, value) in known.Concat(parameters))
        {
            if (sql.Contains($"@{name}", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        return command;
    }
}
