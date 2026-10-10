using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ProcessStartEndpointTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string Instances = "/api/apps/ProcessApp/processes/Submit/instances";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_start_returns_the_running_instance_and_stores_it_with_a_work_item_and_an_audit_record()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        var activeRelease = await fixture.ActiveReleaseIdAsync();
        using var client = fixture.CreateClient();
        using var signIn = Request(HttpMethod.Post, "/api/test-users/sign-in", ProcessStartFixture.Host, """{ "id": "anna" }""");
        using var signedIn = await client.SendAsync(signIn, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

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
            WHERE process_instance_id = @id AND record_id = @subject AND actor = 'anna' AND action = 'process.started'
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
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id", ("id", instance)));
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

    private async Task AssertNothingStoredAsync(Guid subject)
    {
        Assert.Equal(0L, await CountInstancesAsync(subject));
        Assert.Equal(0L, await CountWorkItemsAsync(subject));
        Assert.Equal(0L, await CountReceiptsAsync(subject));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE record_id = @subject", ("subject", subject)));
    }
}
