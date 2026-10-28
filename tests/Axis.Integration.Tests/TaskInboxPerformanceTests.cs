using System.Diagnostics;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// The task list with claims and the in-app notification list stay fast at a realistic size: each
/// answers within the time limit, and the plan of its count query uses its index. The tables also hold
/// rows of other applications, as a shared database does.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class TaskInboxPerformanceTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const int RoleTasks = 1_000;
    private const int ClaimedTasks = 300;
    private const int Notifications = 1_000;
    private const int BackgroundRows = 20_000;
    private const int OtherApplications = 20;
    private const int PageSize = 50;
    private const int Attempts = 3;

    // The Reads objective in performance.md. Each list took about 3 ms locally. The limit is
    // well above that, so shared CI runners do not make the test flaky. The plan check, not the time, is what
    // catches a lost index. The best of three calls counts, because a runner can pause the process.
    private static readonly TimeSpan _queryTime = TimeSpan.FromMilliseconds(300);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_holders_task_list_over_a_thousand_role_tasks_with_three_hundred_claimed_by_others_answers_within_the_time_limit_using_the_task_index()
    {
        // The role tasks of the application under test are the first 1,000 rows, and the first 300 of them
        // are claimed by one of 10 other users. The background rows are open tasks of 20 other applications.
        // With only the rows under test, all of them match and a full table read is the right plan.
        var application = Guid.NewGuid();
        var others = Enumerable.Range(0, OtherApplications).Select(_ => Guid.NewGuid()).ToArray();
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            WITH series AS (
                SELECT n, gen_random_uuid() AS id,
                       CASE WHEN n <= {RoleTasks} THEN @application ELSE (@others::uuid[])[n % {OtherApplications} + 1] END AS application_id
                FROM generate_series(1, {RoleTasks + BackgroundRows}) AS n),
            instances AS (
                INSERT INTO axis.process_instances
                    (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
                SELECT id, application_id, @process, @entity, gen_random_uuid(), @release, '{ProcessStarts.Waiting}', 2, 'approve'
                FROM series)
            INSERT INTO axis.process_tasks
                (id, process_instance_id, process_id, release_id, application_id, step, subject_entity_id, subject_id,
                 assignee_kind, assignee, form_id, state, claimed_by, claimed_at, created_at)
            SELECT gen_random_uuid(), id, @process, @release, application_id, 'approve', @entity, gen_random_uuid(),
                   CASE WHEN n <= {RoleTasks} OR n % 2 = 0 THEN '{ProcessTasks.RoleAssignee}' ELSE '{ProcessTasks.UserAssignee}' END,
                   CASE WHEN n <= {RoleTasks} THEN 'approver' WHEN n % 2 = 0 THEN 'role-' || n % 10 ELSE 'user-' || n % 100 END,
                   @form, '{ProcessTasks.Open}',
                   CASE WHEN n <= {ClaimedTasks} THEN 'other-' || n % 10 END,
                   CASE WHEN n <= {ClaimedTasks} THEN now() END,
                   now() - n * interval '1 second'
            FROM series
            """))
        {
            insert.Parameters.AddWithValue("application", application);
            insert.Parameters.AddWithValue("others", others);
            insert.Parameters.AddWithValue("process", Guid.NewGuid());
            insert.Parameters.AddWithValue("entity", Guid.NewGuid());
            insert.Parameters.AddWithValue("release", Guid.NewGuid());
            insert.Parameters.AddWithValue("form", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await AnalyzeAsync("axis.process_tasks");
        string[] roles = ["approver"];
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);

        var page = await BestOfAsync(
            "task list",
            () => TaskQueries.ListOpenAsync(connection, application, "requester", roles, 1, PageSize, CancellationToken));

        Assert.Equal(RoleTasks - ClaimedTasks, page.TotalCount);
        Assert.Equal(PageSize, page.Items.Count);
        QueryPlans.AssertUsesIndex(
            await QueryPlans.ExplainAsync(
                fixture.DataSource,
                TaskQueries.ListOpenCountSql,
                command => TaskQueries.AddMayActParameters(command, application, "requester", roles),
                CancellationToken),
            "ix_process_tasks_application_id_state_assignee",
            "process_tasks");
    }

    [Fact]
    public async Task A_users_notification_list_over_a_thousand_notifications_half_read_answers_within_the_time_limit_using_the_notification_index()
    {
        // Every second notification goes to the user and the others to the role the user holds. The reads are
        // every notification whose number is 1 or 2 more than a multiple of 4, so half of each kind is read.
        // The background rows are notifications of 20 other applications, so the table holds more than the
        // rows under test.
        var application = Guid.NewGuid();
        var others = Enumerable.Range(0, OtherApplications).Select(_ => Guid.NewGuid()).ToArray();
        await using (var insert = fixture.DataSource.CreateCommand(
            $"""
            WITH series AS (
                SELECT n, gen_random_uuid() AS id,
                       CASE WHEN n <= {Notifications} THEN @application ELSE (@others::uuid[])[n % {OtherApplications} + 1] END AS application_id
                FROM generate_series(1, {Notifications + BackgroundRows}) AS n),
            stored AS (
                INSERT INTO axis.notifications
                    (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", created_at)
                SELECT id, application_id, @release,
                       CASE WHEN n % 2 = 0 THEN '{ProcessNotifications.UserRecipient}' ELSE '{ProcessNotifications.RoleRecipient}' END,
                       CASE WHEN n % 2 = 0 THEN 'requester' ELSE 'approver' END,
                       'request.notice.subject', 'request.notice.body', jsonb_build_object(), now() - n * interval '1 second'
                FROM series)
            INSERT INTO axis.notification_reads (notification_id, user_id)
            SELECT id, 'requester' FROM series WHERE n <= {Notifications} AND n % 4 IN (1, 2)
            """))
        {
            insert.Parameters.AddWithValue("application", application);
            insert.Parameters.AddWithValue("others", others);
            insert.Parameters.AddWithValue("release", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await AnalyzeAsync("axis.notifications");
        await AnalyzeAsync("axis.notification_reads");
        string[] roles = ["approver"];
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);

        var page = await BestOfAsync(
            "notification list",
            () => NotificationQueries.ListAsync(connection, application, "requester", roles, unreadOnly: false, 1, PageSize, CancellationToken));

        Assert.Equal(Notifications, page.TotalCount);
        Assert.Equal(Notifications / 2, page.UnreadCount);
        Assert.Equal(PageSize, page.Items.Count);
        QueryPlans.AssertUsesIndex(
            await QueryPlans.ExplainAsync(
                fixture.DataSource,
                NotificationQueries.CountsSql,
                command =>
                {
                    TaskQueries.AddMayActParameters(command, application, "requester", roles);
                    command.Parameters.AddWithValue("unread", false);
                },
                CancellationToken),
            "ix_notifications_application_id_recipient_created_at",
            "notifications");
    }

    /// <summary>
    /// Makes one call that is not timed, as the first call pays for the connection and the query plan. Then
    /// times <see cref="Attempts"/> calls, fails when the fastest took the time limit or longer, and returns its answer.
    /// </summary>
    private static async Task<T> BestOfAsync<T>(string what, Func<Task<T>> call)
    {
        await call();

        var best = (Elapsed: TimeSpan.MaxValue, Result: default(T)!);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await call();
            stopwatch.Stop();
            if (stopwatch.Elapsed < best.Elapsed)
            {
                best = (stopwatch.Elapsed, result);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"The best of {Attempts} calls of the {what} took {best.Elapsed}.");
        Assert.True(best.Elapsed < _queryTime, $"The {what} took {best.Elapsed} at best of {Attempts} calls.");
        return best.Result;
    }

    /// <summary>Updates the planner's statistics of <paramref name="table"/>, as autovacuum does in production.</summary>
    private async Task AnalyzeAsync(string table)
    {
        await using var command = fixture.DataSource.CreateCommand($"ANALYZE {table}");
        await command.ExecuteNonQueryAsync(CancellationToken);
    }
}
