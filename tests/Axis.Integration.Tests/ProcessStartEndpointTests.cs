using System.Net;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ProcessStartEndpointTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string Instances = "/api/apps/ProcessApp/processes/Submit/instances";
    private const string NightlyJobInstances = "/api/apps/ProcessApp/processes/NightlyJob/instances";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_start_returns_the_running_instance_and_stores_it_with_a_work_item_and_an_audit_record()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        var activeRelease = await fixture.ActiveReleaseIdAsync();
        using var client = fixture.CreateClient().SignedInAs("requester");

        using var response = await StartAsync(subject, client: client);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.Equal(
            ["id", "process", "subjectId", "releaseId", "state"],
            root.EnumerateObject().Select(property => property.Name));
        var id = root.GetProperty("id").GetGuid();
        Assert.Equal("Submit", root.GetProperty("process").GetString());
        Assert.Equal(subject, root.GetProperty("subjectId").GetGuid());
        Assert.Equal(activeRelease, root.GetProperty("releaseId").GetGuid());
        Assert.Equal("running", root.GetProperty("state").GetString());

        Assert.True(fixture.Model.TryGetProcess("Submit", out var process));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE id = @id AND subject_id = @subject AND release_id = @release AND process_id = @process
              AND state = 'running' AND revision = 1 AND step = 'done'
            """,
            ("id", id), ("subject", subject), ("release", activeRelease), ("process", process.Id)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND tenant_id = 'a' AND kind = 'process.step'",
            ("id", id)));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records
            WHERE process_instance_id = @id AND record_id = @subject AND actor = 'requester' AND actor_name = 'Requester User'
              AND action = 'process.started'
              AND details = jsonb_build_object('processId', @process::text, 'releaseId', @release::text)
            """,
            ("id", id), ("subject", subject), ("process", process.Id), ("release", activeRelease)));
    }

    [Fact]
    public async Task Two_concurrent_starts_for_the_same_record_give_one_201_and_one_409()
    {
        var subject = await fixture.InsertRequestAsync(status: null);

        var responses = await Task.WhenAll(StartAsync(subject), StartAsync(subject));

        try
        {
            Assert.Equal(
                [HttpStatusCode.Created, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).Order());
            using var problem = await ReadProblemAsync(
                responses.Single(response => response.StatusCode == HttpStatusCode.Conflict),
                HttpStatusCode.Conflict);
            Assert.Equal(
                "A running or waiting instance of this process already exists for this record.",
                problem.RootElement.GetProperty("title").GetString());
            Assert.Equal(1L, await CountInstancesAsync(subject));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_second_start_while_an_instance_is_running_is_a_409_and_stores_nothing()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        using var first = await StartAsync(subject);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await StartAsync(subject, key: Guid.NewGuid().ToString());

        using var problem = await ReadProblemAsync(second, HttpStatusCode.Conflict);
        Assert.Equal(1L, await CountInstancesAsync(subject));
        Assert.Equal(1L, await CountWorkItemsAsync(subject));
        Assert.Equal(0L, await CountReceiptsAsync(subject));
    }

    [Fact]
    public async Task A_repeat_with_the_same_key_returns_the_first_response_and_stores_nothing_new()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        var key = Guid.NewGuid().ToString();
        using var first = await StartAsync(subject, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync(CancellationToken);
        var instance = JsonDocument.Parse(firstBody).RootElement.GetProperty("id").GetGuid();

        // The record no longer meets the start condition, so only the receipt can answer.
        await fixture.SetStatusAsync(subject, "submitted");
        using var repeat = await StartAsync(subject, key);

        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.Equal("application/json", repeat.Content.Headers.ContentType?.MediaType);
        Assert.Equal(firstBody, await repeat.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(1L, await CountInstancesAsync(subject));
        Assert.Equal(1L, await CountWorkItemsAsync(subject));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND actor = 'requester' AND actor_name = 'Requester User'",
            ("id", instance)));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE record_id = @subject", ("subject", subject)));
        Assert.Equal(1L, await CountReceiptsAsync(subject));
    }

    [Fact]
    public async Task A_key_used_with_another_record_is_a_422_and_stores_nothing()
    {
        var first = await fixture.InsertRequestAsync(status: null);
        var other = await fixture.InsertRequestAsync(status: null);
        var key = Guid.NewGuid().ToString();
        using var started = await StartAsync(first, key);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        using var reused = await StartAsync(other, key);

        using var problem = await ReadProblemAsync(reused, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("This Idempotency-Key was used with a different subject record.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(0L, await CountInstancesAsync(other));
        Assert.Equal(0L, await CountReceiptsAsync(other));
    }

    [Fact]
    public async Task A_record_that_fails_the_start_condition_is_a_400_with_its_text_key_and_stores_nothing()
    {
        var subject = await fixture.InsertRequestAsync(status: "submitted");

        using var response = await StartAsync(subject, key: Guid.NewGuid().ToString());

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        AssertSubjectError(problem, "request.cannotSubmit");
        await AssertNothingStoredAsync(subject);
    }

    [Fact]
    public async Task A_failed_start_can_be_retried_with_the_same_key_after_the_record_is_fixed()
    {
        var subject = await fixture.InsertRequestAsync(status: "submitted");
        var key = Guid.NewGuid().ToString();
        using var failed = await StartAsync(subject, key);
        using var problem = await ReadProblemAsync(failed, HttpStatusCode.BadRequest);

        await fixture.SetStatusAsync(subject, "draft");
        using var retried = await StartAsync(subject, key);

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(1L, await CountInstancesAsync(subject));
        Assert.Equal(1L, await CountReceiptsAsync(subject));
    }

    [Fact]
    public async Task A_start_after_a_new_release_is_activated_is_pinned_to_it_and_the_earlier_instance_keeps_its_release()
    {
        var earlierSubject = await fixture.InsertRequestAsync(status: null);
        var earlierRelease = await fixture.ActiveReleaseIdAsync();
        using var earlier = await StartAsync(earlierSubject);
        Assert.Equal(HttpStatusCode.Created, earlier.StatusCode);
        using var earlierBody = await ReadJsonAsync(earlier);
        var earlierInstance = earlierBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(earlierRelease, earlierBody.RootElement.GetProperty("releaseId").GetGuid());

        var newRelease = await fixture.ActivateChangedReleaseAsync();
        var laterSubject = await fixture.InsertRequestAsync(status: null);
        using var later = await StartAsync(laterSubject);

        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
        using var laterBody = await ReadJsonAsync(later);
        Assert.Equal(newRelease, laterBody.RootElement.GetProperty("releaseId").GetGuid());
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE id = @id AND release_id = @release",
            ("id", earlierInstance), ("release", earlierRelease)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE subject_id = @subject AND release_id = @release",
            ("subject", laterSubject), ("release", newRelease)));
    }

    [Fact]
    public async Task An_unknown_application_or_process_is_a_404()
    {
        var subject = await fixture.InsertRequestAsync(status: null);

        using var unknownProcess = await StartAsync(subject, path: "/api/apps/ProcessApp/processes/Unknown/instances");
        using var unknownApp = await StartAsync(subject, path: "/api/apps/Unknown/processes/Submit/instances");

        using var processProblem = await ReadProblemAsync(unknownProcess, HttpStatusCode.NotFound);
        Assert.Equal("The application has no process with this name.", processProblem.RootElement.GetProperty("title").GetString());
        using var appProblem = await ReadProblemAsync(unknownApp, HttpStatusCode.NotFound);
        Assert.Equal("No application is active under this name.", appProblem.RootElement.GetProperty("title").GetString());
        await AssertNothingStoredAsync(subject);
    }

    [Fact]
    public async Task A_subject_id_that_names_no_record_is_a_400_and_stores_nothing()
    {
        var missing = Guid.NewGuid();

        using var response = await StartAsync(missing, key: Guid.NewGuid().ToString());

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        AssertSubjectError(problem, "No record of the process's entity has this id.");
        await AssertNothingStoredAsync(missing);
    }

    [Fact]
    public async Task A_start_with_no_session_is_a_401_and_stores_nothing()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        using var anonymous = fixture.CreateClient();

        using var response = await StartAsync(subject, key: Guid.NewGuid().ToString(), client: anonymous);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        await AssertNothingStoredAsync(subject);
    }

    [Fact]
    public async Task A_start_by_a_user_without_a_start_grant_is_a_403_and_stores_nothing()
    {
        // Olga may read every request, but holds no start grant on the process.
        var subject = await fixture.InsertRequestAsync(status: null);
        using var olga = fixture.CreateClient().SignedInAs("olga");

        using var response = await StartAsync(subject, key: Guid.NewGuid().ToString(), client: olga);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        await AssertNothingStoredAsync(subject);
    }

    [Fact]
    public async Task A_start_of_a_process_without_a_subject_returns_the_running_instance_and_stores_its_parameters()
    {
        var process = NightlyJob();
        await fixture.EndInstancesAsync(process.Id);
        var activeRelease = await fixture.ActiveReleaseIdAsync();
        using var requester = fixture.CreateClient().SignedInAs("requester");

        using var response = await StartJobAsync("""{ "parameters": { "days": 31 } }""", client: requester);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.Equal(
            ["id", "process", "subjectId", "releaseId", "state"],
            root.EnumerateObject().Select(property => property.Name));
        var id = root.GetProperty("id").GetGuid();
        Assert.Equal("NightlyJob", root.GetProperty("process").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subjectId").ValueKind);
        Assert.Equal(activeRelease, root.GetProperty("releaseId").GetGuid());
        Assert.Equal("running", root.GetProperty("state").GetString());
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE id = @id AND process_id = @process AND subject_id IS NULL AND subject_entity_id IS NULL
              AND release_id = @release AND state = 'running' AND revision = 1 AND parameters = '{"days": 31}'::jsonb
            """,
            ("id", id), ("process", process.Id), ("release", activeRelease)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = 'process.step'",
            ("id", id)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND actor = 'requester' AND action = 'process.started'",
            ("id", id)));
    }

    [Fact]
    public async Task A_mistyped_parameter_of_a_process_without_a_subject_is_a_400_at_the_parameter_and_stores_nothing()
    {
        var process = NightlyJob();
        var before = await CountNightlyJobRowsAsync(process);

        using var response = await StartJobAsync("""{ "parameters": { "days": "x" } }""", key: Guid.NewGuid().ToString());

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        AssertErrors(problem, ("/parameters/days", "Must be a value of type 'integer'."));
        Assert.Equal(before, await CountNightlyJobRowsAsync(process));
    }

    [Theory]
    [InlineData("""{ "parameters": {} }""", "/parameters/days", "Is required.")]
    [InlineData("""{ "parameters": { "days": 1, "other": 2 } }""", "/parameters/other", "Is not a parameter of the process.")]
    [InlineData("""{ "parameters": { "Days": 1 } }""", "/parameters/Days", "Is not a parameter of the process.")]
    [InlineData("""{ "parameters": { "days": null } }""", "/parameters/days", "Must not be null.")]
    [InlineData("""{ "parameters": { "days": 1, "days": 2 } }""", "/parameters/days", "Must be given once.")]
    [InlineData("""{ "parameters": { "days": 1.5 } }""", "/parameters/days", "Must be a value of type 'integer'.")]
    public async Task A_missing_unknown_repeated_null_or_mistyped_parameter_is_a_400_at_the_parameter(string body, string errorKey, string message)
    {
        var process = NightlyJob();
        var before = await CountNightlyJobRowsAsync(process);

        using var response = await StartJobAsync(body);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal([message], errors.GetProperty(errorKey).EnumerateArray().Select(error => error.GetString()));
        Assert.Equal(before, await CountNightlyJobRowsAsync(process));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""{ "parameters": [] }""")]
    [InlineData("""{ "parameters": 1 }""")]
    [InlineData("""{ "parameters": { "days": 1 }, "other": 1 }""")]
    [InlineData("""{ "days": 1 }""")]
    [InlineData("""{ "parameters": { "days": 1 }""")]
    public async Task A_body_that_is_not_one_parameters_object_is_a_400_at_parameters(string body)
    {
        var process = NightlyJob();
        var before = await CountNightlyJobRowsAsync(process);

        using var response = await StartJobAsync(body);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        AssertErrors(problem, ("/parameters", "Must be an object that holds one property, parameters, which is an object."));
        Assert.Equal(before, await CountNightlyJobRowsAsync(process));
    }

    [Fact]
    public async Task A_second_start_of_a_process_without_a_subject_while_one_runs_is_a_409_and_stores_no_second_instance()
    {
        var process = NightlyJob();
        await fixture.EndInstancesAsync(process.Id);
        using var first = await StartJobAsync("""{ "parameters": { "days": 1 } }""");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var before = await CountNightlyJobRowsAsync(process);

        using var second = await StartJobAsync("""{ "parameters": { "days": 2 } }""", key: Guid.NewGuid().ToString());

        using var problem = await ReadProblemAsync(second, HttpStatusCode.Conflict);
        Assert.Equal(
            "A running or waiting instance of this process already exists.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(before, await CountNightlyJobRowsAsync(process));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND state IN ('running', 'waiting')",
            ("process", process.Id)));
    }

    [Fact]
    public async Task A_start_of_a_process_without_a_subject_is_a_403_for_a_user_without_the_start_grant_and_stores_nothing()
    {
        var process = NightlyJob();
        var before = await CountNightlyJobRowsAsync(process);
        using var olga = fixture.CreateClient().SignedInAs("olga");

        using var response = await StartJobAsync("""{ "parameters": { "days": 31 } }""", key: Guid.NewGuid().ToString(), client: olga);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(before, await CountNightlyJobRowsAsync(process));
    }

    [Fact]
    public async Task A_repeat_of_a_process_without_a_subject_with_the_same_parameters_returns_the_first_response_and_other_parameters_are_a_422()
    {
        var process = NightlyJob();
        await fixture.EndInstancesAsync(process.Id);
        var key = Guid.NewGuid().ToString();
        using var first = await StartJobAsync("""{ "parameters": { "days": 31 } }""", key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync(CancellationToken);
        var before = await CountNightlyJobRowsAsync(process);
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_start_receipts WHERE process_id = @process AND key = @key AND subject_id IS NULL",
            ("process", process.Id), ("key", key)));

        using var repeat = await StartJobAsync("""{ "parameters": { "days": 31 } }""", key);
        using var other = await StartJobAsync("""{ "parameters": { "days": 32 } }""", key);

        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);
        Assert.Equal(firstBody, await repeat.Content.ReadAsStringAsync(CancellationToken));
        using var problem = await ReadProblemAsync(other, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("This Idempotency-Key was used with different parameters.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(before, await CountNightlyJobRowsAsync(process));
    }

    [Fact]
    public async Task A_start_on_a_record_outside_the_users_read_filters_is_the_400_of_an_unknown_record_and_stores_nothing()
    {
        // The requester reads only the requests whose title starts with "Request".
        var subject = await fixture.InsertRequestAsync(status: null, title: "Private");

        using var response = await StartAsync(subject, key: Guid.NewGuid().ToString());

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        AssertSubjectError(problem, "No record of the process's entity has this id.");
        await AssertNothingStoredAsync(subject);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""{ "subjectId": 1 }""")]
    [InlineData("""{ "subjectId": "not-a-uuid" }""")]
    [InlineData("""{ "subjectId": "{0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b}" }""")]
    [InlineData("""{ "subjectId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b", "other": 1 }""")]
    [InlineData("""{ "subjectId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b""")]
    public async Task A_body_that_is_not_one_subject_id_is_a_400(string body)
    {
        using var request = Request(HttpMethod.Post, Instances, ProcessStartFixture.Host, body);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        AssertSubjectError(problem, "Must be the id of a record of the process's entity.");
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_415()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        using var request = Request(HttpMethod.Post, Instances, ProcessStartFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""", "text/plain");

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
        await AssertNothingStoredAsync(subject);
    }

    // An empty key is checked in IdempotencyKeyTests: the in-memory test server drops an empty header.
    [Fact]
    public async Task An_idempotency_key_over_255_characters_is_a_400_and_stores_nothing()
    {
        var subject = await fixture.InsertRequestAsync(status: null);

        using var response = await StartAsync(subject, new string('k', 256));

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["Must be 1 to 255 characters."], errors.GetProperty("Idempotency-Key").EnumerateArray().Select(error => error.GetString()));
        await AssertNothingStoredAsync(subject);
    }

    private async Task<HttpResponseMessage> StartAsync(Guid subject, string? key = null, HttpClient? client = null, string path = Instances)
    {
        using var request = Request(HttpMethod.Post, path, ProcessStartFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await (client ?? fixture.Client).SendAsync(request, CancellationToken);
    }

    private async Task<HttpResponseMessage> StartJobAsync(string body, string? key = null, HttpClient? client = null)
    {
        using var request = Request(HttpMethod.Post, NightlyJobInstances, ProcessStartFixture.Host, body);
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await (client ?? fixture.Client).SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    private static void AssertSubjectError(JsonDocument problem, string message)
    {
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["/subjectId"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal([message], errors.GetProperty("/subjectId").EnumerateArray().Select(error => error.GetString()));
    }

    private Task<long> CountInstancesAsync(Guid subject) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE subject_id = @subject", ("subject", subject));

    private Task<long> CountWorkItemsAsync(Guid subject) =>
        fixture.CountAsync(
            "SELECT count(*) FROM axis.process_work_items w JOIN axis.process_instances i ON i.id = w.process_instance_id WHERE i.subject_id = @subject",
            ("subject", subject));

    private Task<long> CountReceiptsAsync(Guid subject) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_start_receipts WHERE subject_id = @subject", ("subject", subject));

    private ProcessModel NightlyJob()
    {
        Assert.True(fixture.Model.TryGetProcess("NightlyJob", out var process));
        return process;
    }

    // The instances and receipts of NightlyJob, so a test can tell that a refused start stored nothing.
    private async Task<(long Instances, long Receipts)> CountNightlyJobRowsAsync(ProcessModel process) =>
        (await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", process.Id)),
            await fixture.CountAsync("SELECT count(*) FROM axis.process_start_receipts WHERE process_id = @process", ("process", process.Id)));

    private static void AssertErrors(JsonDocument problem, params (string Pointer, string Message)[] expected)
    {
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(expected.Select(error => error.Pointer), errors.EnumerateObject().Select(error => error.Name));
        foreach (var (pointer, message) in expected)
        {
            Assert.Equal([message], errors.GetProperty(pointer).EnumerateArray().Select(error => error.GetString()));
        }
    }

    private async Task AssertNothingStoredAsync(Guid subject)
    {
        Assert.Equal(0L, await CountInstancesAsync(subject));
        Assert.Equal(0L, await CountWorkItemsAsync(subject));
        Assert.Equal(0L, await CountReceiptsAsync(subject));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE record_id = @subject", ("subject", subject)));
    }
}
