using System.Diagnostics;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Integration.Tests;

/// <summary>
/// The task list, a record's history, the work claim, the event lookup and the endpoint receipt
/// lookup stay fast at a realistic size: the reads answer within the time limit where a limit is set,
/// and the plan of each query uses its index.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class QueryPlanPerformanceTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const int OpenTasks = 10_000;
    private const int AuditRecordCount = 100_000;
    private const int AuditedRecords = 1_000;
    private const int FutureWorkItems = 100_000;
    private const int EventCount = 100_000;
    private const int ReceiptCount = 100_000;
    private const int DueWorkItems = 100;
    private const int PageSize = 50;

    // Each read is expected to take a few milliseconds. Well above that, so shared CI runners do
    // not make the test flaky. The plan check, not the time, is what catches a lost index.
    private static readonly TimeSpan _queryTime = TimeSpan.FromMilliseconds(500);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_users_task_list_over_ten_thousand_open_tasks_answers_within_the_time_limit_using_the_task_index()
    {
        // Every second task goes to one of 100 users, and the others to one of 10 roles. The user
        // under test holds one role, so they see a few hundred of the tasks.
        static bool MayAct(int n) => n % 2 == 0 ? n / 2 % 100 == 7 : n / 2 % 10 == 3;
        var application = Guid.NewGuid();
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            WITH series AS (SELECT n, gen_random_uuid() AS id FROM generate_series(1, {OpenTasks}) AS n),
            instances AS (
                INSERT INTO axis.process_instances
                    (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
                SELECT id, @application, @process, @entity, gen_random_uuid(), @release, '{ProcessStarts.Waiting}', 2, 'approve'
                FROM series)
            INSERT INTO axis.process_tasks
                (id, process_instance_id, process_id, release_id, application_id, step, subject_entity_id, subject_id,
                 assignee_kind, assignee, form_id, due_at, state, created_at)
            SELECT gen_random_uuid(), id, @process, @release, @application, 'approve', @entity, gen_random_uuid(),
                   CASE WHEN n % 2 = 0 THEN '{ProcessTasks.UserAssignee}' ELSE '{ProcessTasks.RoleAssignee}' END,
                   CASE WHEN n % 2 = 0 THEN 'user-' || n / 2 % 100 ELSE 'role-' || n / 2 % 10 END,
                   @form, CASE WHEN n % 3 = 0 THEN now() + n * interval '1 minute' END, '{ProcessTasks.Open}',
                   now() - n * interval '1 second'
            FROM series
            """))
        {
            insert.Parameters.AddWithValue("application", application);
            insert.Parameters.AddWithValue("process", Guid.NewGuid());
            insert.Parameters.AddWithValue("entity", Guid.NewGuid());
            insert.Parameters.AddWithValue("release", Guid.NewGuid());
            insert.Parameters.AddWithValue("form", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await AnalyzeAsync("axis.process_tasks");
        string[] roles = ["role-3"];
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await TaskQueries.ListOpenAsync(connection, application, "user-7", roles, 1, PageSize, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await TaskQueries.ListOpenAsync(connection, application, "user-7", roles, 1, PageSize, CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The task list took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _queryTime, $"The task list took {stopwatch.Elapsed}.");
        Assert.Equal(Enumerable.Range(1, OpenTasks).Count(MayAct), page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);

        void Bind(NpgsqlCommand command) => TaskQueries.AddMayActParameters(command, application, "user-7", roles);
        QueryPlans.AssertUsesIndex(await QueryPlans.ExplainAsync(fixture.DataSource, TaskQueries.ListOpenCountSql, Bind, CancellationToken), "ix_process_tasks_application_id_state_assignee");
        QueryPlans.AssertUsesIndex(
            await QueryPlans.ExplainAsync(fixture.DataSource, TaskQueries.ListOpenPageSql, command => BindPage(Bind, command), CancellationToken),
            "ix_process_tasks_application_id_state_assignee");
    }

    [Fact]
    public async Task A_records_history_over_a_hundred_thousand_audit_records_answers_within_the_time_limit_using_the_audit_index()
    {
        // The audit records are spread evenly over a thousand records of one entity, newest first,
        // so each record has a hundred of them.
        var entity = Guid.NewGuid();
        var records = Enumerable.Range(0, AuditedRecords).Select(_ => Guid.NewGuid()).ToArray();
        var record = records[7];
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            INSERT INTO axis.audit_records
                (id, occurred_at, actor, action, application_id, entity_id, record_id, process_instance_id, details)
            SELECT gen_random_uuid(), now() - n * interval '1 second', 'anna', '{AuditActions.RecordUpdated}', @application,
                   @entity, (@records::uuid[])[n % {AuditedRecords} + 1], NULL, jsonb_build_object()
            FROM generate_series(1, {AuditRecordCount}) AS n
            """))
        {
            insert.Parameters.AddWithValue("application", Guid.NewGuid());
            insert.Parameters.AddWithValue("entity", entity);
            insert.Parameters.AddWithValue("records", records);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await AnalyzeAsync("axis.audit_records");
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await AuditRecords.ListForRecordAsync(connection, entity, record, 1, PageSize, CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var page = await AuditRecords.ListForRecordAsync(connection, entity, record, 1, PageSize, CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The history took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _queryTime, $"The history took {stopwatch.Elapsed}.");
        Assert.Equal(AuditRecordCount / AuditedRecords, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);

        void Bind(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("entity", entity);
            command.Parameters.AddWithValue("record", record);
        }

        QueryPlans.AssertUsesIndex(await QueryPlans.ExplainAsync(fixture.DataSource, AuditRecords.HistoryCountSql, Bind, CancellationToken), "ix_audit_records_entity_id_record_id_occurred_at_id");
        QueryPlans.AssertUsesIndex(
            await QueryPlans.ExplainAsync(fixture.DataSource, AuditRecords.HistoryPageSql, command => BindPage(Bind, command), CancellationToken),
            "ix_audit_records_entity_id_record_id_occurred_at_id");
    }

    [Fact]
    public async Task The_work_claim_over_a_hundred_thousand_queued_items_uses_the_due_index()
    {
        // A healthy queue is mostly future work, such as deadlines, with a little work due now. On a
        // tiny or fully due queue a full table read is the right plan, so the check would prove nothing.
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id)
            SELECT gen_random_uuid(), @tenant, @kind,
                   CASE WHEN n <= {DueWorkItems} THEN now() - interval '1 minute' ELSE now() + interval '1 day' END, NULL
            FROM generate_series(1, {FutureWorkItems + DueWorkItems}) AS n
            """))
        {
            insert.Parameters.AddWithValue("tenant", ProcessStepFixture.TenantId);
            insert.Parameters.AddWithValue("kind", ProcessStarts.StepWorkItemKind);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        try
        {
            await AnalyzeAsync("axis.process_work_items");

            // EXPLAIN without ANALYZE plans the claim but does not run it, so no item is claimed.
            var plan = await QueryPlans.ExplainAsync(fixture.DataSource, WorkItemQueue.ClaimSql, command =>
            {
                command.Parameters.AddWithValue("lease", NpgsqlDbType.Interval, TimeSpan.FromSeconds(30));
                command.Parameters.AddWithValue("tenant", ProcessStepFixture.TenantId);
                command.Parameters.AddWithValue("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text, new[] { ProcessStarts.StepWorkItemKind });
            }, CancellationToken);

            QueryPlans.AssertUsesIndex(plan, "ix_process_work_items_due_at");
            Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE claim_token IS NOT NULL"));
        }
        finally
        {
            await using var delete = fixture.DataSource.CreateCommand("DELETE FROM axis.process_work_items WHERE process_instance_id IS NULL");
            await delete.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task The_event_lookup_over_a_hundred_thousand_events_uses_the_event_key()
    {
        // The events reference one instance, as the foreign key demands. The lookup reads one by its id.
        var instance = Guid.NewGuid();
        var firstEvent = Guid.NewGuid();
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            WITH instance AS (
                INSERT INTO axis.process_instances
                    (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
                VALUES (@instance, @application, @process, @entity, gen_random_uuid(), @release, '{ProcessStarts.Waiting}', 2, 'approve'))
            INSERT INTO axis.events
                (id, application_id, event, release_id, entity_id, subject_id, payload, process_instance_id, raised_at)
            SELECT CASE WHEN n = 1 THEN @first ELSE gen_random_uuid() END, @application, 'RequestNoted', @release, @entity,
                   gen_random_uuid(), jsonb_build_object(), @instance, now()
            FROM generate_series(1, {EventCount}) AS n
            """))
        {
            insert.Parameters.AddWithValue("instance", instance);
            insert.Parameters.AddWithValue("first", firstEvent);
            insert.Parameters.AddWithValue("application", Guid.NewGuid());
            insert.Parameters.AddWithValue("process", Guid.NewGuid());
            insert.Parameters.AddWithValue("entity", Guid.NewGuid());
            insert.Parameters.AddWithValue("release", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await AnalyzeAsync("axis.events");

        var plan = await QueryPlans.ExplainAsync(fixture.DataSource, ProcessEvents.LoadSql, command => command.Parameters.AddWithValue("id", firstEvent), CancellationToken);

        QueryPlans.AssertUsesIndex(plan, "pk_events");
    }

    [Fact]
    public async Task The_endpoint_receipt_lookup_over_a_hundred_thousand_receipts_uses_the_receipt_key()
    {
        // The receipts spread over 10 endpoints and 20 callers, each with its own keys.
        var application = Guid.NewGuid();
        var endpoints = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            INSERT INTO axis.endpoint_receipts (application_id, endpoint_id, caller, key, record_id, instance_id, input_hash)
            SELECT @application, (@endpoints::uuid[])[n % 10 + 1], 'caller-' || n % 20, 'key-' || n,
                   gen_random_uuid(), gen_random_uuid(), sha256('x')
            FROM generate_series(1, {ReceiptCount}) AS n
            """))
        {
            insert.Parameters.AddWithValue("application", application);
            insert.Parameters.AddWithValue("endpoints", endpoints);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await AnalyzeAsync("axis.endpoint_receipts");

        // Row 7 has endpoint 8 of 10 and caller 7 of 20.
        var plan = await QueryPlans.ExplainAsync(fixture.DataSource, EndpointReceipts.FindSql, command =>
        {
            command.Parameters.AddWithValue("application", application);
            command.Parameters.AddWithValue("endpoint", endpoints[7]);
            command.Parameters.AddWithValue("caller", "caller-7");
            command.Parameters.AddWithValue("key", "key-7");
            command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = "{}" });
        }, CancellationToken);

        QueryPlans.AssertUsesIndex(plan, "pk_endpoint_receipts");
    }

    /// <summary>Adds the parameters of the first page to a page query.</summary>
    private static void BindPage(Action<NpgsqlCommand> bind, NpgsqlCommand command)
    {
        bind(command);
        command.Parameters.AddWithValue("limit", PageSize);
        command.Parameters.AddWithValue("offset", 0L);
    }

    /// <summary>Updates the planner's statistics of <paramref name="table"/>, as autovacuum does in production.</summary>
    private async Task AnalyzeAsync(string table)
    {
        await using var command = fixture.DataSource.CreateCommand($"ANALYZE {table}");
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
