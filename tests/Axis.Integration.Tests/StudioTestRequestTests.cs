using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The test route of the authoring API stores a <c>test</c> publish request with a snapshot of the folder and the
/// scenario names. Until a publisher runs it, it stays queued.
/// </summary>
public sealed class StudioTestRequestTests : IAsyncLifetime
{
    private const string TestUrl = "/api/studio/apps/PurchaseRequests/test";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_author_stores_a_queued_test_request_with_a_snapshot_and_the_scenario_names()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_test_store", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", StudioPublishTests.HostA, connectionString)]);
        using var lan = factory.CreateClient().SignedInAs("lan");

        using var accepted = await StudioPublishTests.SendAsync(
            lan, HttpMethod.Post, TestUrl, StudioPublishTests.HostA, """{ "scenarios": ["Second", "First"] }""");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var request = await StudioPublishTests.ReadAsync(accepted);
        var id = request.RootElement.GetProperty("id").GetString()!;
        Assert.Equal($"/api/studio/apps/PurchaseRequests/requests/{id}", accepted.Headers.Location?.OriginalString);
        Assert.Equal("test", request.RootElement.GetProperty("kind").GetString());
        Assert.Equal("tenant", request.RootElement.GetProperty("target").GetString());
        Assert.Equal("queued", request.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, request.RootElement.GetProperty("scenarios").GetArrayLength());
        Assert.Equal(
            "test|tenant|queued|true|[\"Second\", \"First\"]",
            await StoredRowAsync(connectionString));
    }

    [Fact]
    public async Task A_test_without_a_list_or_with_an_empty_list_stores_no_names()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_test_all", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", StudioPublishTests.HostA, connectionString)]);
        using var lan = factory.CreateClient().SignedInAs("lan");

        foreach (var body in new string?[] { null, "{}", """{ "scenarios": [] }""" })
        {
            using var accepted = await StudioPublishTests.SendAsync(
                lan, HttpMethod.Post, TestUrl, StudioPublishTests.HostA, body, contentType: body is null ? null : "application/json");
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        Assert.Equal(3L, await StudioPublishTests.CountRequestsAsync(connectionString));
        Assert.Equal(
            3L,
            await PostgreSqlServer.AdminScalarAsync(
                connectionString,
                "SELECT count(*) FROM axis.publish_requests WHERE kind = 'test' AND scenario_names IS NULL",
                CancellationToken));
    }

    [Fact]
    public async Task The_test_route_refuses_and_stores_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_test_refuse", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", StudioPublishTests.HostA, connectionString)]);
        using var nobody = factory.CreateClient();
        using var anna = factory.CreateClient().SignedInAs("anna");
        using var hoa = factory.CreateClient().SignedInAs("hoa");

        using var signedOut = await StudioPublishTests.SendAsync(nobody, HttpMethod.Post, TestUrl, StudioPublishTests.HostA);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);

        using var notAuthor = await StudioPublishTests.SendAsync(anna, HttpMethod.Post, TestUrl, StudioPublishTests.HostA);
        Assert.Equal(HttpStatusCode.Forbidden, notAuthor.StatusCode);

        using var withoutCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(TestUrl, UriKind.Relative));
        withoutCsrf.Headers.Host = StudioPublishTests.HostA;
        using var noHeader = await hoa.SendAsync(withoutCsrf, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);

        using var unknown = await StudioPublishTests.SendAsync(
            hoa, HttpMethod.Post, "/api/studio/apps/NoSuchApplication/test", StudioPublishTests.HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(connectionString));
    }

    [Fact]
    public async Task A_body_that_is_not_a_test_request_is_refused_and_stores_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_test_body", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", StudioPublishTests.HostA, connectionString)]);
        using var hoa = factory.CreateClient().SignedInAs("hoa");

        using var plain = await StudioPublishTests.SendAsync(
            hoa, HttpMethod.Post, TestUrl, StudioPublishTests.HostA, """{ "scenarios": [] }""", contentType: "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, plain.StatusCode);

        foreach (var body in new[] { "not json", """{"scenarios":1}""", """{"scenarios":[1]}""", """{"scenarios":null}""", """{"other":[]}""", "[]" })
        {
            using var refused = await StudioPublishTests.SendAsync(hoa, HttpMethod.Post, TestUrl, StudioPublishTests.HostA, body);
            Assert.True(HttpStatusCode.BadRequest == refused.StatusCode, $"{body} answered {(int)refused.StatusCode}.");
        }

        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(connectionString));
    }

    [Fact]
    public async Task The_status_route_answers_scenarios_null_for_a_validate_and_an_empty_list_for_a_test_before_and_after_it_runs()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_test_status", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", StudioPublishTests.HostA, connectionString)]);
        using var hoa = factory.CreateClient().SignedInAs("hoa");

        using var test = await StudioPublishTests.SendAsync(hoa, HttpMethod.Post, TestUrl, StudioPublishTests.HostA);
        Assert.Equal(HttpStatusCode.Accepted, test.StatusCode);
        using var validate = await StudioPublishTests.SendAsync(
            hoa, HttpMethod.Post, "/api/studio/apps/PurchaseRequests/validate", StudioPublishTests.HostA);
        Assert.Equal(HttpStatusCode.Accepted, validate.StatusCode);
        using var validateAccepted = await StudioPublishTests.ReadAsync(validate);
        Assert.Equal(JsonValueKind.Null, validateAccepted.RootElement.GetProperty("scenarios").ValueKind);

        using var queued = await StudioPublishTests.GetJsonAsync(hoa, test.Headers.Location!.OriginalString, StudioPublishTests.HostA);
        Assert.Equal("queued", queued.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, queued.RootElement.GetProperty("scenarios").ValueKind);
        Assert.Equal(0, queued.RootElement.GetProperty("scenarios").GetArrayLength());

        // This publisher has no scenario server, so the test ends failed, with no scenario result.
        await using var publisher = StudioPublishTests.RunPublisher("a", StudioPublishTests.HostA, connectionString);
        using var finished = await StudioPublishTests.WaitForFinalAsync(
            hoa, validate.Headers.Location!.OriginalString, StudioPublishTests.HostA);
        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, finished.RootElement.GetProperty("scenarios").ValueKind);

        using var ended = await StudioPublishTests.WaitForFinalAsync(
            hoa, test.Headers.Location!.OriginalString, StudioPublishTests.HostA);
        Assert.Equal("failed", ended.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, ended.RootElement.GetProperty("scenarios").ValueKind);
        Assert.Equal(0, ended.RootElement.GetProperty("scenarios").GetArrayLength());
    }

    private static async Task<string?> StoredRowAsync(string connectionString) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            "SELECT kind || '|' || target || '|' || status || '|' || (snapshot IS NOT NULL) || '|' || scenario_names::text FROM axis.publish_requests",
            CancellationToken);
}
