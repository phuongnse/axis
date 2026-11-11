using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The preview route of the authoring API stores a publish request with target <c>preview</c>, and the publisher runs
/// it on the tenant's preview tenant. It needs the author role only, and it never changes the main tenant. The server
/// and the publisher run together here.
/// </summary>
public sealed class StudioPreviewTests : IAsyncLifetime
{
    private const string HostA = StudioPublishTests.HostA;

    private const string PreviewUrl = "/api/studio/apps/PurchaseRequests/preview";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_author_without_the_publisher_role_previews_and_the_main_tenant_does_not_change()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("studio_preview_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("studio_preview_preview", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, main)], preview);
        using var employee = factory.CreateClient().SignedInAs("employee");
        var activeBefore = await AdminScalarAsync(main, "SELECT release_id FROM axis.active_releases");
        var activationsBefore = await AdminScalarAsync(main, "SELECT count(*) FROM axis.release_activations");
        var previewBefore = await AdminScalarAsync(preview, "SELECT release_id FROM axis.active_releases");
        Assert.NotNull(activeBefore);
        Assert.NotNull(previewBefore);
        await StudioPublishTests.EditTextsAsync(folder.Path, "Departments", "Teams");

        using var accepted = await StudioPublishTests.SendAsync(employee, HttpMethod.Post, PreviewUrl, HostA);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var request = await StudioPublishTests.ReadAsync(accepted);
        var id = request.RootElement.GetProperty("id").GetString()!;
        Assert.Equal($"/api/studio/apps/PurchaseRequests/requests/{id}", accepted.Headers.Location?.OriginalString);
        Assert.Equal("preview", request.RootElement.GetProperty("target").GetString());
        Assert.Equal("publish", request.RootElement.GetProperty("kind").GetString());
        Assert.Equal("queued", request.RootElement.GetProperty("status").GetString());
        Assert.Equal(1L, await StudioPublishTests.CountRequestsAsync(main));

        await using var publisher = StudioPublishTests.RunPublisher("a", HostA, main, preview);
        using var finished = await StudioPublishTests.WaitForFinalAsync(employee, accepted.Headers.Location!.OriginalString, HostA);

        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal("preview", finished.RootElement.GetProperty("target").GetString());
        var releaseId = finished.RootElement.GetProperty("releaseId").GetString();
        Assert.NotNull(releaseId);
        var previewAfter = await AdminScalarAsync(preview, "SELECT release_id FROM axis.active_releases");
        Assert.NotEqual(previewBefore, previewAfter);
        Assert.Equal(releaseId, previewAfter!.ToString());
        Assert.Equal(activeBefore, await AdminScalarAsync(main, "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(activationsBefore, await AdminScalarAsync(main, "SELECT count(*) FROM axis.release_activations"));

        // The main tenant still serves the old text.
        using var texts = await StudioPublishTests.GetJsonAsync(employee, "/api/sites/purchasing/texts/en", HostA);
        Assert.Equal("Departments", texts.RootElement.GetProperty("texts").GetProperty("nav.departments").GetString());
    }

    [Fact]
    public async Task The_route_answers_409_for_a_tenant_without_a_preview_tenant_and_stores_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("studio_preview_none", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, main)]);
        using var author = factory.CreateClient().SignedInAs("author");

        using var refused = await StudioPublishTests.SendAsync(author, HttpMethod.Post, PreviewUrl, HostA);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("This tenant has no preview tenant.", (await StudioPublishTests.ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(main));

        using var unknown = await StudioPublishTests.SendAsync(author, HttpMethod.Post, "/api/studio/apps/NoSuchApplication/preview", HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var me = await StudioPublishTests.GetJsonAsync(author, "/api/me", HostA);
        Assert.Equal(JsonValueKind.Null, me.RootElement.GetProperty("studioPreviewHost").ValueKind);
        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(main));
    }

    [Fact]
    public async Task A_preview_of_a_folder_that_does_not_compile_fails_and_the_preview_tenant_keeps_its_release()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("studio_preview_broken_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("studio_preview_broken_preview", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, main)], preview);
        using var employee = factory.CreateClient().SignedInAs("employee");
        var activeBefore = await AdminScalarAsync(preview, "SELECT release_id FROM axis.active_releases");
        var activationsBefore = await AdminScalarAsync(preview, "SELECT count(*) FROM axis.release_activations");
        Assert.NotNull(activeBefore);

        // The server activated the folder at start, so the edit is the first change.
        var entityPath = Path.Combine(folder.Path, "entities", "purchase-request.json");
        var entity = await File.ReadAllTextAsync(entityPath, CancellationToken);
        var broken = entity.Replace("\"name\": \"title\", \"type\": \"text\"", "\"name\": \"title\", \"type\": \"nonsense\"", StringComparison.Ordinal);
        Assert.NotEqual(entity, broken);
        await File.WriteAllTextAsync(entityPath, broken, CancellationToken);

        using var accepted = await StudioPublishTests.SendAsync(employee, HttpMethod.Post, PreviewUrl, HostA);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await using var publisher = StudioPublishTests.RunPublisher("a", HostA, main, preview);
        using var finished = await StudioPublishTests.WaitForFinalAsync(employee, accepted.Headers.Location!.OriginalString, HostA);

        Assert.Equal("failed", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal("preview", finished.RootElement.GetProperty("target").GetString());
        var diagnostics = finished.RootElement.GetProperty("diagnostics");
        Assert.Contains(diagnostics.EnumerateArray(), item =>
            item.GetProperty("file").GetString() == "entities/purchase-request.json" && item.GetProperty("severity").GetString() == "error");
        Assert.Equal(activeBefore, await AdminScalarAsync(preview, "SELECT release_id FROM axis.active_releases"));
        Assert.Equal(activationsBefore, await AdminScalarAsync(preview, "SELECT count(*) FROM axis.release_activations"));
    }

    [Fact]
    public async Task The_me_route_says_users_is_false_on_the_preview_tenant_for_a_holder_of_the_administrator_role()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("studio_preview_users_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("studio_preview_users_preview", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, main)], preview)
            .WithWebHostBuilder(builder => builder.UseSetting("Users:AdministratorRole", "author"));
        using var author = factory.CreateClient().SignedInAs("author");

        using var onMain = await StudioPublishTests.GetJsonAsync(author, "/api/me", HostA);
        using var onPreview = await StudioPublishTests.GetJsonAsync(author, "/api/me", StudioPublishTests.PreviewHost);

        Assert.True(onMain.RootElement.GetProperty("users").GetBoolean());
        Assert.False(onPreview.RootElement.GetProperty("users").GetBoolean());
    }

    [Fact]
    public async Task The_me_route_gives_the_preview_host_to_an_author_only()
    {
        using var folder = StudioPublishTests.NewFolder();
        var main = await _database.CreateDatabaseAsync("studio_preview_me_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("studio_preview_me_preview", CancellationToken);
        await using var factory = StudioPublishTests.NewFactory(folder.Path, [("a", HostA, main)], preview);

        foreach (var author in new[] { "employee", "author" })
        {
            using var client = factory.CreateClient().SignedInAs(author);
            using var me = await StudioPublishTests.GetJsonAsync(client, "/api/me", HostA);
            Assert.Equal(StudioPublishTests.PreviewHost, me.RootElement.GetProperty("studioPreviewHost").GetString());
        }

        using var requester = factory.CreateClient().SignedInAs("requester");
        using var annaMe = await StudioPublishTests.GetJsonAsync(requester, "/api/me", HostA);
        Assert.False(annaMe.RootElement.GetProperty("studio").GetBoolean());
        Assert.Equal(JsonValueKind.Null, annaMe.RootElement.GetProperty("studioPreviewHost").ValueKind);

        // A person who is not an author has no right to preview.
        using var refused = await StudioPublishTests.SendAsync(requester, HttpMethod.Post, PreviewUrl, HostA);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(main));
    }

    private static Task<object?> AdminScalarAsync(string connectionString, string sql) =>
        PostgreSqlServer.AdminScalarAsync(connectionString, sql, CancellationToken);
}
