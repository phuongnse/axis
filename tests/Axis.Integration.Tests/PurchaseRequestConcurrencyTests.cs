using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Duplicate and concurrent submits, edits and task decisions on the purchase request sample,
/// sent through the HTTP API as double clicks, retries and two people at once would send them.
/// </summary>
public sealed class PurchaseRequestConcurrencyTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Records = $"{App}/entities/PurchaseRequest/records";

    private const string Instances = $"{App}/processes/PurchaseRequestApproval/instances";

    private static readonly string _requests = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5")));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ten_concurrent_submits_of_one_request_start_one_instance_and_create_one_manager_task()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateRequestAsync(requester, 500);
        var clients = new List<HttpClient>();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                clients.Add(fixture.CreateClient().SignedInAs("requester"));
            }

            // Half repeat one key, as retries after a lost response do. The rest are separate clicks.
            var key = $"K-{Guid.NewGuid():N}";
            var keys = Enumerable.Range(0, 10).Select(i => i < 5 ? key : $"U-{Guid.NewGuid():N}").ToList();
            var responses = await Task.WhenAll(clients.Select((client, i) => PostStartAsync(client, subject, keys[i])));
            var results = new List<(string Key, HttpStatusCode Status, Guid? Id)>();
            for (var i = 0; i < responses.Length; i++)
            {
                using var response = responses[i];
                Guid? id = response.StatusCode == HttpStatusCode.Created ? await ReadIdAsync(response) : null;
                results.Add((keys[i], response.StatusCode, id));
            }

            Assert.All(results, result => Assert.Contains(result.Status, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            var instance = Assert.Single(await fixture.InstancesAsync(subject));
            Assert.All(results.Where(result => result.Id is not null), result => Assert.Equal(instance.Id, result.Id));
            Assert.Equal(1L, await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE record_id = @subject AND action = 'process.started'",
                ("subject", subject)));

            await fixture.RunUntilIdleAsync(CancellationToken);

            Assert.Equal(1L, await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.process_tasks
                WHERE process_instance_id = @instance AND step = 'managerApproval' AND state = 'open'
                  AND assignee_kind = 'user' AND assignee = 'manager-2'
                """,
                ("instance", instance.Id)));
            Assert.Equal(1L, await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_tasks WHERE process_instance_id = @instance",
                ("instance", instance.Id)));

            // Once the key's start won, every repeat of it gets that start's response. Otherwise
            // the key was never stored, and the submitted request fails the start condition.
            var keyWon = results.Any(result => result.Key == key && result.Status == HttpStatusCode.Created);
            using var repeat = await PostStartAsync(requester, subject, key);
            if (keyWon)
            {
                Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
                Assert.Equal(instance.Id, await ReadIdAsync(repeat));
            }
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
            }

            Assert.Single(await fixture.InstancesAsync(subject));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_submit_waits_for_an_uncommitted_edit_and_checks_the_start_condition_on_the_edited_request()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateRequestAsync(requester, 500);

        await using var connection = await fixture.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using (var edit = connection.CreateCommand())
        {
            edit.Transaction = transaction;
            edit.CommandText = $"""
                UPDATE {_requests}
                SET {EntityNaming.Quote(EntityNaming.Column("status"))} = 'rejected', {EntityNaming.Quote(EntityNaming.VersionColumn)} = {EntityNaming.Quote(EntityNaming.VersionColumn)} + 1
                WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id
                """;
            edit.Parameters.AddWithValue("id", subject);
            Assert.Equal(1, await edit.ExecuteNonQueryAsync(CancellationToken));
        }

        var start = PostStartAsync(requester, subject);
        await WaitUntilAsync(async () => start.IsCompleted || await fixture.AdminCountAsync(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 1, "The start did not wait on the lock.");
        await transaction.CommitAsync(CancellationToken);

        using var response = await start;
        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["/subjectId"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal(["purchaseRequest.cannotSubmit"], errors.GetProperty("/subjectId").EnumerateArray().Select(error => error.GetString()));
        Assert.Empty(await fixture.InstancesAsync(subject));
    }

    [Fact]
    public async Task A_title_edit_racing_a_submit_and_the_worker_succeeds_or_conflicts_and_is_never_lost()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var editor = fixture.CreateClient().SignedInAs("requester");
        for (var round = 0; round < 5; round++)
        {
            var subject = await CreateRequestAsync(requester, 500);
            var (original, _, version) = await ReadAsync(requester, subject);
            Assert.Equal(1L, version);
            var title = $"Edited {Guid.NewGuid():N}";

            var start = PostStartAsync(requester, subject);
            var edit = SendAsync(editor, HttpMethod.Patch, $"{Records}/{subject:D}", $$"""{ "version": 1, "values": { "title": "{{title}}" } }""");
            await Task.WhenAll(start, edit, RunWorkerAsync());
            await fixture.RunUntilIdleAsync(CancellationToken);

            using var started = await start;
            using var edited = await edit;
            Assert.Equal(HttpStatusCode.Created, started.StatusCode);
            // A submit that wins first puts the request outside the employee's update filter, so the edit is 403.
            Assert.Contains(edited.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Forbidden });

            var (finalTitle, status, finalVersion) = await ReadAsync(requester, subject);
            Assert.Equal("submitted", status);
            var instance = Assert.Single(await fixture.InstancesAsync(subject));
            Assert.Equal(finalVersion, (long)(await fixture.ScalarAsync(
                "SELECT (output->>'subjectVersion')::bigint FROM axis.process_step_history WHERE process_instance_id = @instance AND step = 'submit'",
                ("instance", instance.Id)))!);
            Assert.Equal(edited.StatusCode == HttpStatusCode.OK ? title : original, finalTitle);
            Assert.Equal(Enumerable.Range(1, (int)finalVersion).Select(v => (long)v), await AuditedVersionsAsync(subject));
        }
    }

    [Fact]
    public async Task Two_finance_reviewers_completing_one_task_at_once_record_one_decision()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        using var reviewer = fixture.CreateClient().SignedInAs("reviewer");
        var subject = await CreateRequestAsync(requester, 10000);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        using (var approved = await CompleteAsync(manager2, await OpenTaskAsync(instance, "managerApproval"), """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = await OpenTaskAsync(instance, "financeApproval");

        var responses = await Task.WhenAll(
            CompleteAsync(financeReviewer, task, """{ "outcome": "approve" }"""),
            CompleteAsync(reviewer, task, """{ "outcome": "approve" }"""));
        using var first = responses[0];
        using var second = responses[1];

        var winner = Assert.Single(new[] { ("finance-reviewer", first), ("reviewer", second) }, pair => pair.Item2.StatusCode == HttpStatusCode.OK).Item1;
        var loser = first.StatusCode == HttpStatusCode.OK ? second : first;
        using (var problem = await RecordApiFixture.ReadProblemAsync(loser, HttpStatusCode.Conflict))
        {
            Assert.Equal("This task is already completed.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(winner, await fixture.ScalarAsync("SELECT completed_by FROM axis.process_tasks WHERE id = @task", ("task", task)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @instance AND step = 'financeApproval' AND decision IS NOT NULL",
            ("instance", instance)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @instance AND action = 'task.completed' AND actor IN ('finance-reviewer', 'reviewer')",
            ("instance", instance)));
        Assert.Equal(winner, await fixture.ScalarAsync(
            "SELECT actor FROM axis.audit_records WHERE process_instance_id = @instance AND action = 'task.completed' AND actor IN ('finance-reviewer', 'reviewer')",
            ("instance", instance)));

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("approved", (await ReadAsync(requester, subject)).Status);
    }

    [Fact]
    public async Task Concurrent_manager_completions_give_one_success_and_a_later_repeat_changes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = await OpenTaskAsync(instance, "managerApproval");

        var clients = new List<HttpClient>();
        try
        {
            for (var i = 0; i < 6; i++)
            {
                clients.Add(fixture.CreateClient().SignedInAs("manager-2"));
            }

            var responses = await Task.WhenAll(clients.Take(5).Select(client => CompleteAsync(client, task, """{ "outcome": "approve" }""")));
            var statuses = responses.Select(response => response.StatusCode).ToList();
            foreach (var response in responses)
            {
                response.Dispose();
            }

            Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
            Assert.Equal(4, statuses.Count(status => status == HttpStatusCode.Conflict));

            var before = await SnapshotAsync(instance);
            using var repeat = await CompleteAsync(clients[5], task, """{ "outcome": "approve" }""");
            Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
            Assert.Equal(before, await SnapshotAsync(instance));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>Runs the worker a few times while a race is in flight, as a running worker would.</summary>
    private async Task RunWorkerAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            await fixture.RunUntilIdleAsync(CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken);
        }
    }

    /// <summary>The instance's state and revision, its task rows and its audit record count, as one text.</summary>
    private async Task<string?> SnapshotAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync(
            """
            SELECT i.state || ':' || i.step || ':' || i.revision
              || '|' || (SELECT string_agg(concat_ws(',', t.id, t.step, t.state, t.outcome, t.completed_by, t.completed_at), ';' ORDER BY t.created_at, t.id)
                         FROM axis.process_tasks t WHERE t.process_instance_id = i.id)
              || '|' || (SELECT count(*) FROM axis.audit_records a WHERE a.process_instance_id = i.id)
            FROM axis.process_instances i WHERE i.id = @instance
            """,
            ("instance", instance));

    /// <summary>The versions in the record's audit records, in order.</summary>
    private async Task<List<long>> AuditedVersionsAsync(Guid subject)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (details->>'version')::bigint FROM axis.audit_records
            WHERE record_id = @subject AND action LIKE 'record.%'
            ORDER BY 1
            """;
        command.Parameters.AddWithValue("subject", subject);
        var versions = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions;
    }

    /// <summary>The id of the instance's one open task at <paramref name="step"/>.</summary>
    private async Task<Guid> OpenTaskAsync(Guid instance, string step) =>
        (Guid)(await fixture.ScalarAsync(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @instance AND state = 'open' AND step = @step",
            ("instance", instance),
            ("step", step)))!;

    /// <summary>Starts the approval process for <paramref name="subject"/>, with <paramref name="key"/> as its <c>Idempotency-Key</c> when given.</summary>
    private static Task<HttpResponseMessage> PostStartAsync(HttpClient client, Guid subject, string? key = null) =>
        SendAsync(client, HttpMethod.Post, Instances, $$"""{ "subjectId": "{{subject:D}}" }""", key);

    /// <summary>The request's title, status and version, read through the record API.</summary>
    private static async Task<(string? Title, string? Status, long Version)> ReadAsync(HttpClient client, Guid subject)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Records}/{subject:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        return (values.GetProperty("title").GetString(), values.GetProperty("status").GetString(), body.RootElement.GetProperty("version").GetInt64());
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null, string? idempotencyKey = null)
    {
        using var request = RecordApiFixture.Request(method, path, Host, body);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
