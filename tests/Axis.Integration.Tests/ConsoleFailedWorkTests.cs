using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ConsoleFailedWorkTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string Path = "/api/console/failed-work";

    // Seeded once for the fixture, so every test sees the same four items of failed work.
    private static readonly ConditionalWeakTable<ProcessStartFixture, Task<Seed>> _seeds = [];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_operator_gets_the_failed_work_of_every_application_newest_first()
    {
        var seed = await SeedAsync();

        var (status, body) = await GetAsync("olga", string.Empty);

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        var root = body.RootElement;
        Assert.Equal(["items", "page", "pageSize", "totalCount"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal((1, 20, 4L), (root.GetProperty("page").GetInt32(), root.GetProperty("pageSize").GetInt32(), root.GetProperty("totalCount").GetInt64()));
        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["connector", "instance", "package", "instance"], items.Select(item => item.GetProperty("kind").GetString()));

        var connector = items[0];
        Assert.Equal(seed.FirstName, connector.GetProperty("application").GetString());
        Assert.Equal(seed.FirstApplicationId, connector.GetProperty("applicationId").GetGuid());
        Assert.Equal("Submit", connector.GetProperty("process").GetString());
        Assert.Equal(seed.ConnectorInstanceId, connector.GetProperty("instanceId").GetGuid());
        Assert.Equal("send", connector.GetProperty("step").GetString());
        Assert.Equal(seed.ConnectorCallId, connector.GetProperty("callId").GetGuid());
        Assert.Equal("Purchasing", connector.GetProperty("connector").GetString());
        Assert.Null(connector.GetProperty("package").GetString());
        Assert.Equal("createOrder", connector.GetProperty("operation").GetString());
        Assert.Equal(Hour(4), connector.GetProperty("failedAt").GetDateTimeOffset());
        Assert.Equal("Timed out.", connector.GetProperty("error").GetString());

        var failedSecond = items[1];
        Assert.Equal(seed.SecondName, failedSecond.GetProperty("application").GetString());
        Assert.Equal("NightlyJob", failedSecond.GetProperty("process").GetString());
        Assert.Equal(seed.SecondFailedInstanceId, failedSecond.GetProperty("instanceId").GetGuid());
        Assert.Equal("check", failedSecond.GetProperty("step").GetString());
        Assert.Equal(JsonValueKind.Null, failedSecond.GetProperty("callId").ValueKind);
        Assert.Equal(JsonValueKind.Null, failedSecond.GetProperty("connector").ValueKind);
        Assert.Equal(JsonValueKind.Null, failedSecond.GetProperty("package").ValueKind);
        Assert.Equal(JsonValueKind.Null, failedSecond.GetProperty("operation").ValueKind);
        Assert.Equal(Hour(3), failedSecond.GetProperty("failedAt").GetDateTimeOffset());
        Assert.Equal("Newest error.", failedSecond.GetProperty("error").GetString());

        var package = items[2];
        Assert.Equal(seed.SecondName, package.GetProperty("application").GetString());
        Assert.Equal(seed.SecondPackageInstanceId, package.GetProperty("instanceId").GetGuid());
        Assert.Equal("pay", package.GetProperty("step").GetString());
        Assert.Equal(seed.PackageCallId, package.GetProperty("callId").GetGuid());
        Assert.Equal(JsonValueKind.Null, package.GetProperty("connector").ValueKind);
        Assert.Equal("ledger", package.GetProperty("package").GetString());
        Assert.Equal("post", package.GetProperty("operation").GetString());
        Assert.Equal(Hour(2), package.GetProperty("failedAt").GetDateTimeOffset());
        Assert.Equal("The package failed.", package.GetProperty("error").GetString());

        var failedFirst = items[3];
        Assert.Equal(seed.FirstName, failedFirst.GetProperty("application").GetString());
        Assert.Equal("Submit", failedFirst.GetProperty("process").GetString());
        Assert.Equal(seed.FirstFailedInstanceId, failedFirst.GetProperty("instanceId").GetGuid());
        Assert.Equal(Hour(1), failedFirst.GetProperty("failedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, failedFirst.GetProperty("error").ValueKind);

        Assert.All(items, item => Assert.Equal(
            ["kind", "applicationId", "application", "process", "instanceId", "step", "callId", "connector", "package", "operation", "failedAt", "error"],
            item.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task The_application_filter_keeps_only_the_work_of_that_application()
    {
        var seed = await SeedAsync();

        var (status, body) = await GetAsync("olga", $"?application={seed.SecondApplicationId}");

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt64());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([seed.SecondFailedInstanceId, seed.SecondPackageInstanceId], items.Select(item => item.GetProperty("instanceId").GetGuid()));
        Assert.All(items, item => Assert.Equal(seed.SecondApplicationId, item.GetProperty("applicationId").GetGuid()));
    }

    [Fact]
    public async Task Paging_narrows_the_page_and_keeps_the_total()
    {
        var seed = await SeedAsync();

        var (status, body) = await GetAsync("olga", "?pageSize=1&page=2");

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        Assert.Equal((2, 1, 4L), (body.RootElement.GetProperty("page").GetInt32(), body.RootElement.GetProperty("pageSize").GetInt32(), body.RootElement.GetProperty("totalCount").GetInt64()));
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(seed.SecondFailedInstanceId, item.GetProperty("instanceId").GetGuid());
    }

    [Fact]
    public async Task Instances_and_calls_in_other_states_are_not_listed_and_no_item_has_an_input_or_output()
    {
        await SeedAsync();

        var (_, body) = await GetAsync("olga", "?pageSize=100");

        using var _ = body;
        Assert.Equal(4, body.RootElement.GetProperty("totalCount").GetInt64());
        var text = body.RootElement.GetRawText();
        Assert.DoesNotContain("\"input\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"output\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-input", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_without_the_operator_role_gets_403_and_a_request_without_a_session_gets_401()
    {
        await SeedAsync();

        var (forbidden, forbiddenBody) = await GetAsync("requester", string.Empty);
        forbiddenBody?.Dispose();
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient();
        using var request = Request(Path, ProcessStartFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_application_that_is_not_an_id_is_a_400_keyed_application()
    {
        await SeedAsync();

        var (status, body) = await GetAsync("olga", "?application=nope&page=0");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        using var _ = body;
        var errors = body.RootElement.GetProperty("errors");
        Assert.Equal(["application", "page"], errors.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    private static DateTimeOffset Hour(int hour) => new(2100, 1, 1, hour, 0, 0, TimeSpan.Zero);

    private WebApplicationFactory<Program> OperatorRoleIsReader() =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Console:OperatorRole", "reader"));

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetAsync(string user, string query)
    {
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs(user);
        using var request = Request(Path + query, ProcessStartFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        var content = await response.Content.ReadAsStringAsync(CancellationToken);
        return (response.StatusCode, JsonDocument.Parse(content));
    }

    private Task<Seed> SeedAsync() => _seeds.GetValue(fixture, SeedCoreAsync);

    private static async Task<Seed> SeedCoreAsync(ProcessStartFixture fixture)
    {
        var firstRelease = fixture.FirstReleaseId;
        var first = fixture.Model;
        Assert.True(first.TryGetProcess("Submit", out var submit));
        Assert.True(first.TryGetEntity(submit.Entity!.Name, out var entity));
        var (secondRelease, second) = await fixture.ActivateApplicationAsync("SecondApp");
        Assert.True(second.TryGetProcess("NightlyJob", out var job));

        await using var connection = await fixture.OpenConnectionAsync();
        Task<Guid> Submit(string state, int? endedHour = null) =>
            InsertInstanceAsync(connection, first.Manifest.Id, submit.Id, entity.Id, Guid.NewGuid(), firstRelease, state, "done", endedHour);
        Task<Guid> Job(string state, int? endedHour = null) =>
            InsertInstanceAsync(connection, second.Manifest.Id, job.Id, null, null, secondRelease, state, "check", endedHour);

        // The failed instance of the first application has no history error. The second has two, and the newer one shows.
        var firstFailed = await Submit("failed", 1);
        var secondFailed = await Job("failed", 3);
        await InsertHistoryAsync(connection, secondFailed, "check", 1, Hour(1), "Older error.");
        await InsertHistoryAsync(connection, secondFailed, "check", 2, Hour(2), "Newest error.");
        await InsertHistoryAsync(connection, secondFailed, "check", 3, Hour(3), null);
        await InsertHistoryAsync(connection, firstFailed, "done", 1, Hour(1), null);

        // The unknown calls: the connector call has an attempt row that sets its time, and the package call has none.
        var connectorInstance = await Submit("waiting");
        var connectorCall = await InsertCallAsync(connection, "connector_calls", connectorInstance, "send", firstRelease, "unknown", "Timed out.", Hour(0));
        await InsertAttemptAsync(connection, "connector", connectorCall, 1, Hour(3), "retryable");
        await InsertAttemptAsync(connection, "connector", connectorCall, 2, Hour(4), "unknown");
        await InsertAttemptAsync(connection, "connector", connectorCall, 1, Hour(5), "found", kind: "lookup");
        var packageInstance = await Job("waiting");
        var packageCall = await InsertCallAsync(connection, "package_calls", packageInstance, "pay", secondRelease, "unknown", "The package failed.", Hour(2));

        // Work that the list leaves out.
        await Submit("running");
        await Submit("completed", 6);
        await Submit("cancelled", 7);
        await Job("completed", 8);
        await Job("cancelled", 9);
        var otherInstance = await Submit("waiting");
        foreach (var (state, step) in new[] { ("pending", "a"), ("sending", "b"), ("succeeded", "c"), ("failed", "d"), ("cancelled", "e") })
        {
            await InsertCallAsync(connection, "connector_calls", otherInstance, step, firstRelease, state, "Ignored.", Hour(10));
            await InsertCallAsync(connection, "package_calls", otherInstance, step, firstRelease, state, "Ignored.", Hour(10));
        }

        return new Seed(
            first.Manifest.Id,
            first.Manifest.Name,
            second.Manifest.Id,
            second.Manifest.Name,
            firstFailed,
            secondFailed,
            connectorInstance,
            connectorCall,
            packageInstance,
            packageCall);
    }

    private static async Task<Guid> InsertInstanceAsync(
        NpgsqlConnection connection,
        Guid application,
        Guid process,
        Guid? entity,
        Guid? subject,
        Guid release,
        string state,
        string step,
        int? endedHour)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, created_at, ended_at)
            VALUES (@id, @application, @process, @entity, @subject, @release, @state, 1, @step, @created, @ended)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", application);
        command.Parameters.AddWithValue("process", process);
        command.Parameters.AddWithValue("entity", (object?)entity ?? DBNull.Value);
        command.Parameters.AddWithValue("subject", (object?)subject ?? DBNull.Value);
        command.Parameters.AddWithValue("release", release);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("created", Hour(0));
        command.Parameters.AddWithValue("ended", endedHour is { } hour ? Hour(hour) : DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task InsertHistoryAsync(NpgsqlConnection connection, Guid instance, string step, long revision, DateTimeOffset finishedAt, string? error)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_step_history (id, process_instance_id, step, revision, input, output, error, started_at, finished_at)
            VALUES (@id, @instance, @step, @revision, '{"secret-input": 1}'::jsonb, '{"next": "secret-output"}'::jsonb, @error, @finished, @finished)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("instance", instance);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("finished", finishedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertCallAsync(
        NpgsqlConnection connection, string table, Guid instance, string step, Guid release, string state, string error, DateTimeOffset createdAt)
    {
        var id = Guid.CreateVersion7();
        var (columns, values) = table == "connector_calls"
            ? ("connector, operation", "'Purchasing', 'createOrder'")
            : ("package, version, operation", "'ledger', '1.0.0', 'post'");
        await using var command = new NpgsqlCommand(
            $$"""
            INSERT INTO axis.{{table}} (id, process_instance_id, step, staged_revision, release_id, {{columns}}, state, attempts, input, last_error, created_at)
            VALUES (@id, @instance, @step, 1, @release, {{values}}, @state, 1, '{"secret-input": 1}'::jsonb, @error, @created)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("instance", instance);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("release", release);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task InsertAttemptAsync(
        NpgsqlConnection connection, string callKind, Guid call, int number, DateTimeOffset finishedAt, string outcome, string kind = "attempt")
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_call_attempts (id, call_kind, call_id, kind, number, started_at, finished_at, outcome)
            VALUES (@id, @callKind, @call, @kind, @number, @finished, @finished, @outcome)
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("callKind", callKind);
        command.Parameters.AddWithValue("call", call);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("finished", finishedAt);
        command.Parameters.AddWithValue("outcome", outcome);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Seed(
        Guid FirstApplicationId,
        string FirstName,
        Guid SecondApplicationId,
        string SecondName,
        Guid FirstFailedInstanceId,
        Guid SecondFailedInstanceId,
        Guid ConnectorInstanceId,
        Guid ConnectorCallId,
        Guid SecondPackageInstanceId,
        Guid PackageCallId);
}
