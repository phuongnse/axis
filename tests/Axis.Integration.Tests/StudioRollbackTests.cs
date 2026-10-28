using System.Net;
using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.StudioPublishTests;

namespace Axis.Integration.Tests;

/// <summary>
/// The rollback route of the authoring API stores a <c>rollback</c> request for a release of the history, and the publisher
/// activates that release again. The server and the publisher run together here.
/// </summary>
public sealed class StudioRollbackTests : IAsyncLifetime
{
    private const string ApplicationUrl = "/api/studio/apps/PurchaseRequests";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task A_rollback_makes_the_older_release_live_again_while_instances_stay_on_their_release()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_rollback", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        await using var publisher = RunPublisher("a", HostA, connectionString);

        // Release 1 is the startup activation. Release 2 is published with another label.
        using var startup = await GetJsonAsync(author, $"{ApplicationUrl}/releases", HostA);
        var first = startup.RootElement.GetProperty("active").GetProperty("releaseId").GetGuid();
        await EditTextsAsync(folder.Path, "Departments", "Teams");
        var second = await PublishAsync(author);
        Assert.NotEqual(first, second);
        await AssertNavigationAsync(author, "Teams");

        var subject = await SampleAppFixture.CreateRequestAsync(requester, 500m);
        var oldInstance = await SampleAppFixture.StartAsync(requester, subject);
        Assert.Equal(second, await InstanceReleaseAsync(connectionString, oldInstance));
        using var published = await GetJsonAsync(author, $"{ApplicationUrl}/releases", HostA);
        var folderHash = published.RootElement.GetProperty("folderContentHash").GetString();

        using var accepted = await SendAsync(author, HttpMethod.Post, $"{ApplicationUrl}/releases/{first:D}/rollback", HostA);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var request = await ReadAsync(accepted);
        var requestId = request.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"{ApplicationUrl}/requests/{requestId:D}", accepted.Headers.Location?.OriginalString);
        Assert.Equal(("rollback", "queued", "tenant", "author"), (
            request.RootElement.GetProperty("kind").GetString(),
            request.RootElement.GetProperty("status").GetString(),
            request.RootElement.GetProperty("target").GetString(),
            request.RootElement.GetProperty("requestedBy").GetString()));
        Assert.Equal(first.ToString("D"), request.RootElement.GetProperty("releaseId").GetString());
        using var finished = await WaitForFinalAsync(author, accepted.Headers.Location!.OriginalString, HostA);
        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal(first.ToString("D"), finished.RootElement.GetProperty("releaseId").GetString());

        // The site shows release 1's labels, and the history shows release 1 active again under its old number.
        await AssertNavigationAsync(author, "Departments");
        using var history = await GetJsonAsync(author, $"{ApplicationUrl}/releases", HostA);
        var active = history.RootElement.GetProperty("active");
        Assert.Equal((first, 1), (active.GetProperty("releaseId").GetGuid(), active.GetProperty("number").GetInt32()));
        var items = history.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([1, 2, 1], items.Select(item => item.GetProperty("number").GetInt32()));
        var newest = items[0];
        Assert.Equal(
            (first, "author", "Author User", requestId, JsonValueKind.Null, JsonValueKind.Null),
            (newest.GetProperty("releaseId").GetGuid(), newest.GetProperty("actor").GetString(), newest.GetProperty("actorName").GetString(),
             newest.GetProperty("requestId").GetGuid(), newest.GetProperty("label").ValueKind, newest.GetProperty("note").ValueKind));
        Assert.NotEqual(active.GetProperty("contentHash").GetString(), history.RootElement.GetProperty("folderContentHash").GetString());
        Assert.Equal(folderHash, history.RootElement.GetProperty("folderContentHash").GetString());

        // The folder is untouched, and the instance of release 2 keeps its release. A new start pins release 1.
        Assert.Contains("\"Teams\"", await File.ReadAllTextAsync(Path.Combine(folder.Path, "texts", "en.json"), CancellationToken), StringComparison.Ordinal);
        Assert.Equal((second, ProcessStarts.Running), (await InstanceReleaseAsync(connectionString, oldInstance), await InstanceStateAsync(connectionString, oldInstance)));
        var other = await SampleAppFixture.CreateRequestAsync(requester, 500m);
        var newInstance = await SampleAppFixture.StartAsync(requester, other);
        Assert.Equal(first, await InstanceReleaseAsync(connectionString, newInstance));

        Assert.Equal(1L, await CountAuditAsync(connectionString, "release.rolledBack"));
        Assert.Equal(0L, await CountAuditAsync(connectionString, "release.rollbackFailed"));
        var details = JsonDocument.Parse((string)(await PostgreSqlServer.AdminScalarAsync(
            connectionString, "SELECT details::text FROM axis.audit_records WHERE action = 'release.rolledBack'", CancellationToken))!);
        Assert.Equal(
            (requestId.ToString("D"), first.ToString("D"), 1),
            (details.RootElement.GetProperty("requestId").GetString(), details.RootElement.GetProperty("releaseId").GetString(), details.RootElement.GetProperty("number").GetInt32()));
        Assert.Equal("author", await PostgreSqlServer.AdminScalarAsync(connectionString, "SELECT actor FROM axis.audit_records WHERE action = 'release.rolledBack'", CancellationToken));
    }

    [Fact]
    public async Task A_rollback_that_the_activation_refuses_ends_failed_with_one_audit_record_and_keeps_the_active_release()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_rollback_refused", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        await using var publisher = RunPublisher("a", HostA, connectionString);
        using var startup = await GetJsonAsync(author, $"{ApplicationUrl}/releases", HostA);
        var first = startup.RootElement.GetProperty("active").GetProperty("releaseId").GetGuid();

        // A new field has no column in release 1, so release 1 cannot run on the tables of release 2.
        await AddFieldAsync(folder.Path);
        var second = await PublishAsync(author);

        using var accepted = await SendAsync(author, HttpMethod.Post, $"{ApplicationUrl}/releases/{first:D}/rollback", HostA);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var finished = await WaitForFinalAsync(author, accepted.Headers.Location!.OriginalString, HostA);

        Assert.Equal("failed", finished.RootElement.GetProperty("status").GetString());
        Assert.Contains(finished.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("code").GetString() == DiagnosticCodes.RemovedField);
        using var history = await GetJsonAsync(author, $"{ApplicationUrl}/releases", HostA);
        Assert.Equal(second, history.RootElement.GetProperty("active").GetProperty("releaseId").GetGuid());
        Assert.Equal(2, history.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1L, await CountAuditAsync(connectionString, "release.rollbackFailed"));
        Assert.Equal(0L, await CountAuditAsync(connectionString, "release.rolledBack"));
    }

    [Fact]
    public async Task A_rollback_needs_the_publisher_role_and_a_release_of_the_history()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_rollback_roles", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        using var employee = factory.CreateClient().SignedInAs("employee");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var nobody = factory.CreateClient();
        using var startup = await GetJsonAsync(author, $"{ApplicationUrl}/releases", HostA);
        var release = startup.RootElement.GetProperty("active").GetProperty("releaseId").GetGuid();
        var url = $"{ApplicationUrl}/releases/{release:D}/rollback";

        using var withoutPublisher = await SendAsync(employee, HttpMethod.Post, url, HostA);
        Assert.Equal(HttpStatusCode.Forbidden, withoutPublisher.StatusCode);
        Assert.Equal("You do not hold the publisher role.", (await ReadAsync(withoutPublisher)).RootElement.GetProperty("title").GetString());
        using var withoutAuthor = await SendAsync(requester, HttpMethod.Post, url, HostA);
        Assert.Equal(HttpStatusCode.Forbidden, withoutAuthor.StatusCode);
        using var signedOut = await SendAsync(nobody, HttpMethod.Post, url, HostA);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        using var withoutCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative));
        withoutCsrf.Headers.Host = HostA;
        using var noHeader = await author.SendAsync(withoutCsrf, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Equal(0L, await CountRequestsAsync(connectionString));

        foreach (var missing in new[] { $"{ApplicationUrl}/releases/{Guid.NewGuid():D}/rollback", $"{ApplicationUrl}/releases/not-a-uuid/rollback" })
        {
            using var unknown = await SendAsync(author, HttpMethod.Post, missing, HostA);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        using var unknownApplication = await SendAsync(author, HttpMethod.Post, $"/api/studio/apps/NoSuchApplication/releases/{release:D}/rollback", HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknownApplication.StatusCode);
        Assert.Equal(0L, await CountRequestsAsync(connectionString));

        // The body is ignored.
        using var withBody = await SendAsync(author, HttpMethod.Post, url, HostA, "not json");
        Assert.Equal(HttpStatusCode.Accepted, withBody.StatusCode);
        Assert.Equal(1L, await CountRequestsAsync(connectionString));
    }

    // Publishes the folder and waits for the request to succeed. Returns the release that is active afterwards.
    private static async Task<Guid> PublishAsync(HttpClient client)
    {
        using var accepted = await SendAsync(client, HttpMethod.Post, $"{ApplicationUrl}/publish", HostA);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var finished = await WaitForFinalAsync(client, accepted.Headers.Location!.OriginalString, HostA);
        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        return finished.RootElement.GetProperty("releaseId").GetGuid();
    }

    private static async Task AssertNavigationAsync(HttpClient client, string label)
    {
        using var texts = await GetJsonAsync(client, "/api/sites/purchasing/texts/en", HostA);
        Assert.Equal(label, texts.RootElement.GetProperty("texts").GetProperty("nav.departments").GetString());
    }

    // Adds an optional text field to the Department entity of the sample.
    private static async Task AddFieldAsync(string folder)
    {
        var path = Path.Combine(folder, "entities", "department.json");
        var text = await File.ReadAllTextAsync(path, CancellationToken);
        var index = text.IndexOf("\"fields\": [", StringComparison.Ordinal);
        Assert.True(index >= 0);
        var edited = text.Insert(index + "\"fields\": [".Length, """ { "name": "remark", "type": "text" },""");
        await File.WriteAllTextAsync(path, edited, CancellationToken);
    }

    private static async Task<Guid> InstanceReleaseAsync(string connectionString, Guid instance) =>
        (Guid)(await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT release_id FROM axis.process_instances WHERE id = '{instance}'", CancellationToken))!;

    private static async Task<string?> InstanceStateAsync(string connectionString, Guid instance) =>
        (string?)await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT state FROM axis.process_instances WHERE id = '{instance}'", CancellationToken);

    private static async Task<long> CountAuditAsync(string connectionString, string action) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT count(*) FROM axis.audit_records WHERE action = '{action}'", CancellationToken))!;
}
