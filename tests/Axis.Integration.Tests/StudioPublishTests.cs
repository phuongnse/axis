using System.Net;
using System.Text.Json;
using Axis.Admin;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Axis.Integration.Tests;

/// <summary>
/// The validate and publish routes of the authoring API store a snapshot of the folder as a publish request in the
/// database of the host's tenant, and the publisher runs it. The server and the publisher run together here.
/// </summary>
public sealed class StudioPublishTests : IAsyncLifetime
{
    internal const string HostA = "a.example.test";

    internal const string PreviewHost = "preview.example.test";

    private const string HostB = "b.example.test";

    private const string ValidateUrl = "/api/studio/apps/PurchaseRequests/validate";

    private const string PublishUrl = "/api/studio/apps/PurchaseRequests/publish";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task A_published_folder_goes_live_as_it_was_at_the_call_and_a_later_edit_is_not_in_the_release()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_publish", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var client = factory.CreateClient().SignedInAs("author");

        await EditTextsAsync(folder.Path, "Departments", "Teams");
        using var accepted = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA, """{ "label": "Teams", "note": "Renamed." }""");
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var request = await ReadAsync(accepted);
        var id = request.RootElement.GetProperty("id").GetString()!;
        Assert.Equal($"/api/studio/apps/PurchaseRequests/requests/{id}", accepted.Headers.Location?.OriginalString);
        Assert.Equal("queued", request.RootElement.GetProperty("status").GetString());
        Assert.Equal("publish", request.RootElement.GetProperty("kind").GetString());
        Assert.Equal("tenant", request.RootElement.GetProperty("target").GetString());
        Assert.Equal("PurchaseRequests", request.RootElement.GetProperty("application").GetString());
        Assert.Equal("Teams", request.RootElement.GetProperty("label").GetString());
        Assert.Equal("Renamed.", request.RootElement.GetProperty("note").GetString());
        Assert.Equal("author", request.RootElement.GetProperty("requestedBy").GetString());
        Assert.Equal(0, request.RootElement.GetProperty("diagnostics").GetArrayLength());

        // The call has answered, so this edit is not in the request.
        await EditTextsAsync(folder.Path, "Teams", "Units");
        await using var publisher = RunPublisher("a", HostA, connectionString);
        using var finished = await WaitForFinalAsync(client, accepted.Headers.Location!.OriginalString, HostA);

        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, finished.RootElement.GetProperty("releaseId").ValueKind);
        Assert.Equal(JsonValueKind.String, finished.RootElement.GetProperty("finishedAt").ValueKind);
        using var texts = await GetJsonAsync(client, "/api/sites/purchasing/texts/en", HostA);
        Assert.Equal("Teams", texts.RootElement.GetProperty("texts").GetProperty("nav.departments").GetString());
    }

    [Fact]
    public async Task An_author_without_the_publisher_role_validates_but_cannot_publish()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_publish_roles", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var employee = factory.CreateClient().SignedInAs("employee");
        using var nobody = factory.CreateClient();
        using var author = factory.CreateClient().SignedInAs("author");

        using var refused = await SendAsync(employee, HttpMethod.Post, PublishUrl, HostA);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("You do not hold the publisher role.", (await ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        Assert.Equal(0L, await CountRequestsAsync(connectionString));

        using var accepted = await SendAsync(employee, HttpMethod.Post, ValidateUrl, HostA);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.NotNull(accepted.Headers.Location);
        Assert.Equal("validate", (await ReadAsync(accepted)).RootElement.GetProperty("kind").GetString());
        await using var publisher = RunPublisher("a", HostA, connectionString);
        using var finished = await WaitForFinalAsync(employee, accepted.Headers.Location!.OriginalString, HostA);
        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, finished.RootElement.GetProperty("releaseId").ValueKind);
        Assert.Equal(1L, await CountRequestsAsync(connectionString));

        foreach (var url in new[] { ValidateUrl, PublishUrl })
        {
            using var signedOut = await SendAsync(nobody, HttpMethod.Post, url, HostA);
            Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);

            using var withoutCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative));
            withoutCsrf.Headers.Host = HostA;
            using var noHeader = await author.SendAsync(withoutCsrf, CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        }

        using var unknown = await SendAsync(author, HttpMethod.Post, "/api/studio/apps/NoSuchApplication/publish", HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(1L, await CountRequestsAsync(connectionString));
    }

    [Fact]
    public async Task The_me_route_says_who_may_publish()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_publish_me", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);

        using var author = factory.CreateClient().SignedInAs("author");
        using var authorMe = await GetJsonAsync(author, "/api/me", HostA);
        Assert.True(authorMe.RootElement.GetProperty("studio").GetBoolean());
        Assert.True(authorMe.RootElement.GetProperty("studioPublisher").GetBoolean());

        using var employee = factory.CreateClient().SignedInAs("employee");
        using var employeeMe = await GetJsonAsync(employee, "/api/me", HostA);
        Assert.True(employeeMe.RootElement.GetProperty("studio").GetBoolean());
        Assert.False(employeeMe.RootElement.GetProperty("studioPublisher").GetBoolean());

        using var requester = factory.CreateClient().SignedInAs("requester");
        using var annaMe = await GetJsonAsync(requester, "/api/me", HostA);
        Assert.False(annaMe.RootElement.GetProperty("studioPublisher").GetBoolean());
    }

    [Fact]
    public async Task A_publish_on_one_tenants_host_stores_a_request_in_that_tenants_database_only()
    {
        using var folder = NewFolder();
        var databaseA = await _database.CreateDatabaseAsync("studio_publish_a", CancellationToken);
        var databaseB = await _database.CreateDatabaseAsync("studio_publish_b", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, databaseA), ("b", HostB, databaseB)]);
        using var client = factory.CreateClient().SignedInAs("author");

        using var accepted = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1L, await CountRequestsAsync(databaseA));
        Assert.Equal(0L, await CountRequestsAsync(databaseB));

        // The request exists on tenant A's host only. On tenant B's host its id is unknown.
        using var found = await SendAsync(client, HttpMethod.Get, accepted.Headers.Location!.OriginalString, HostA);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        using var other = await SendAsync(client, HttpMethod.Get, accepted.Headers.Location!.OriginalString, HostB);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_anything_is_stored()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_publish_bad", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var client = factory.CreateClient().SignedInAs("author");

        foreach (var body in new[] { "[]", "not json", """{ "other": "x" }""", """{ "label": 1 }""", """{ "label": null }""", $$"""{ "label": "{{new string('x', 101)}}" }""", $$"""{ "note": "{{new string('x', 2001)}}" }""" })
        {
            using var response = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("The body is not a valid publish request.", (await ReadAsync(response)).RootElement.GetProperty("title").GetString());
        }

        using var wrongType = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA, """{ "label": "x" }""", "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);

        // A validate ignores its body.
        using var validate = await SendAsync(client, HttpMethod.Post, ValidateUrl, HostA, "not json");
        Assert.Equal(HttpStatusCode.Accepted, validate.StatusCode);
        Assert.Equal(1L, await CountRequestsAsync(connectionString));

        using var unknownId = await SendAsync(client, HttpMethod.Get, "/api/studio/apps/PurchaseRequests/requests/not-a-uuid", HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknownId.StatusCode);
        using var unknownRequest = await SendAsync(client, HttpMethod.Get, $"/api/studio/apps/PurchaseRequests/requests/{Guid.NewGuid():D}", HostA);
        Assert.Equal(HttpStatusCode.NotFound, unknownRequest.StatusCode);

        // A publish without a body stores no label and no note.
        using var empty = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);
        Assert.Equal(HttpStatusCode.Accepted, empty.StatusCode);
        using var stored = await ReadAsync(empty);
        Assert.Equal(JsonValueKind.Null, stored.RootElement.GetProperty("label").ValueKind);
        Assert.Equal(JsonValueKind.Null, stored.RootElement.GetProperty("note").ValueKind);

        // A manifest without a UUID id has no application id for the queue.
        var manifestPath = Path.Combine(folder.Path, "application.json");
        var manifest = await File.ReadAllTextAsync(manifestPath, CancellationToken);
        using (var document = JsonDocument.Parse(manifest))
        {
            var id = document.RootElement.GetProperty("id").GetString()!;
            await File.WriteAllTextAsync(manifestPath, manifest.Replace(id, "not-a-uuid", StringComparison.Ordinal), CancellationToken);
        }

        foreach (var url in new[] { ValidateUrl, PublishUrl })
        {
            using var response = await SendAsync(client, HttpMethod.Post, url, HostA);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("The application manifest has no valid id.", (await ReadAsync(response)).RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(2L, await CountRequestsAsync(connectionString));
    }

    internal static TemporaryFolder NewFolder()
    {
        var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        return folder;
    }

    // A server for the tenants, which activates the folder on each of them at start.
    // With a previewConnectionString, the first tenant also has a preview tenant, with no deployment bindings.
    internal static WebApplicationFactory<Program> NewFactory(
        string folder, (string Tenant, string Host, string ConnectionString)[] tenants, string? previewConnectionString = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(tenants[0].ConnectionString);
            foreach (var (tenant, host, connectionString) in tenants)
            {
                builder.UseSetting($"Tenants:{tenant}:Hosts:0", host);
                builder.UseSetting($"Tenants:{tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
                builder.UseSetting($"Tenants:{tenant}:SchemaConnectionString", connectionString);
                builder.UseSetting($"Tenants:{tenant}:Bindings:PurchasingSystemUrl:Kind", "url");
                builder.UseSetting($"Tenants:{tenant}:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
                builder.UseSetting($"Tenants:{tenant}:Bindings:PurchasingSystemToken:Kind", "secret");
                builder.UseSetting($"Tenants:{tenant}:Bindings:PurchasingSystemToken:Value", "studio-test-token");
                builder.UseTestOidc(tenant);
            }

            if (previewConnectionString is not null)
            {
                var preview = $"{tenants[0].Tenant}-preview";
                builder.UseSetting($"Tenants:{preview}:Hosts:0", PreviewHost);
                builder.UseSetting($"Tenants:{preview}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(previewConnectionString));
                builder.UseSetting($"Tenants:{preview}:SchemaConnectionString", previewConnectionString);
                builder.UseSetting($"Tenants:{preview}:PreviewOf", tenants[0].Tenant);
            }

            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("ActivateOnStartup:0", folder);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:PublisherRole", "publisher");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author", "publisher"),
                new TestIdentity("employee", "Employee User", "author"),
                new TestIdentity("requester", "Requester", "employee"));
        });

    // Runs the publisher of one tenant in this process until it is disposed. The server has migrated the database by now.
    // With a previewConnectionString, it serves the tenant's preview tenant too.
    internal static RunningPublisher RunPublisher(string tenant, string host, string connectionString, string? previewConnectionString = null)
    {
        var settings = new Dictionary<string, string?>
        {
            // The databases run without TLS, which only Development and Testing allow.
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["Tenancy:Source"] = "configuration",
            [$"Tenants:{tenant}:Hosts:0"] = host,
            [$"Tenants:{tenant}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
            [$"Tenants:{tenant}:SchemaConnectionString"] = connectionString,
            [$"Tenants:{tenant}:Bindings:PurchasingSystemUrl:Kind"] = "url",
            [$"Tenants:{tenant}:Bindings:PurchasingSystemUrl:Value"] = "https://purchasing.example.test",
            [$"Tenants:{tenant}:Bindings:PurchasingSystemToken:Kind"] = "secret",
            [$"Tenants:{tenant}:Bindings:PurchasingSystemToken:Value"] = "studio-test-token",
            ["Extensions:Path"] = SamplePackages.Path,
            ["Modules:Path"] = SampleModules.Path,
        };
        if (previewConnectionString is not null)
        {
            var preview = $"{tenant}-preview";
            settings[$"Tenants:{preview}:Hosts:0"] = PreviewHost;
            settings[$"Tenants:{preview}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(previewConnectionString);
            settings[$"Tenants:{preview}:SchemaConnectionString"] = previewConnectionString;
            settings[$"Tenants:{preview}:PreviewOf"] = tenant;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var output = TextWriter.Synchronized(new StringWriter());
        var error = new StringWriter();
        return new RunningPublisher(stop, AdminCommands.RunAsync(["publisher"], configuration, output, error, stop.Token));
    }

    internal sealed class RunningPublisher(CancellationTokenSource stop, Task<int> run) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            await run;
            stop.Dispose();
        }
    }

    // Polls the request through the API until it is succeeded or failed.
    internal static async Task<JsonDocument> WaitForFinalAsync(HttpClient client, string location, string host)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var status = await GetJsonAsync(client, location, host);
            if (status.RootElement.GetProperty("status").GetString() is "succeeded" or "failed")
            {
                return status;
            }

            status.Dispose();
            await Task.Delay(200, CancellationToken);
        }

        Assert.Fail("The request did not end in time. Is the publisher running?");
        return null!;
    }

    internal static async Task EditTextsAsync(string folder, string from, string to)
    {
        var path = Path.Combine(folder, "texts", "en.json");
        var texts = await File.ReadAllTextAsync(path, CancellationToken);
        var edited = texts.Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal);
        Assert.NotEqual(texts, edited);
        await File.WriteAllTextAsync(path, edited, CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, string host, string? body = null, string? contentType = "application/json")
    {
        using var request = RecordApiFixture.Request(method, path, host, body, contentType);
        return await client.SendAsync(request, CancellationToken);
    }

    internal static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, string host)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path, host);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    internal static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    internal static async Task<long> CountRequestsAsync(string connectionString) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, "SELECT count(*) FROM axis.publish_requests", CancellationToken))!;

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
