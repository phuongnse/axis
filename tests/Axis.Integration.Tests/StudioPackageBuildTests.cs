using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Axis.Studio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>POST /api/studio/apps/{application}/build</c> builds one package of the working folder without a publish, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. The server is tenant <c>default</c> on a real PostgreSQL database and
/// the publisher runs the request against the host of the test process, with a stub Trivy and the real pip, while the publisher's
/// <c>PackageBuild:Python</c> names a missing command. The test identity <c>developer</c> holds the author and developer roles and
/// <c>author</c> the author role only. The tests use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class StudioPackageBuildTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string BuildUrl = "/api/studio/apps/PurchaseRequests/build";

    private const string NoDeveloper = "You do not hold the developer role.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_build_of_a_package_with_a_syntax_error_ends_failed_at_the_file_with_its_line_and_column_and_the_output_and_releases_nothing()
    {
        using var folder = NewPackageFolder("def broken(:\n    pass\n");
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var releases = await CountAsync("axis.releases");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });

        using var accepted = await PostAsync(client, """{"package":"tools"}""");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var queued = await ReadAsync(accepted);
        Assert.Equal("build", queued.RootElement.GetProperty("kind").GetString());
        Assert.Equal("queued", queued.RootElement.GetProperty("status").GetString());
        Assert.Empty(queued.RootElement.GetProperty("output").EnumerateArray());
        await using var publisher = RunPublisher(host);
        using var finished = await WaitForFinalAsync(client, accepted.Headers.Location!.OriginalString);

        var root = finished.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("releaseId").ValueKind);
        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("AXC0149", diagnostic.GetProperty("code").GetString());
        Assert.Equal("packages/tools/adapter.py", diagnostic.GetProperty("file").GetString());
        Assert.Equal(1, diagnostic.GetProperty("line").GetInt32());

        // The column of a syntax error differs between Python versions, so only that it is a position is checked.
        Assert.True(diagnostic.GetProperty("column").GetInt32() > 0);
        Assert.NotEmpty(root.GetProperty("output").EnumerateArray());
        Assert.Equal(releases, await CountAsync("axis.releases"));
        Assert.Empty(host.Tests());
    }

    [Fact]
    public async Task A_build_of_a_valid_package_ends_succeeded_with_the_compile_line_and_releases_nothing()
    {
        using var folder = NewPackageFolder("import sys\n");
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var releases = await CountAsync("axis.releases");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });

        using var accepted = await PostAsync(client, """{"package":"tools"}""");
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await using var publisher = RunPublisher(host);
        using var finished = await WaitForFinalAsync(client, accepted.Headers.Location!.OriginalString);

        var root = finished.RootElement;
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("releaseId").ValueKind);
        Assert.Contains(
            root.GetProperty("output").EnumerateArray(),
            line => line.GetString()!.StartsWith("Compiled ", StringComparison.Ordinal));
        Assert.Equal(releases, await CountAsync("axis.releases"));
    }

    [Fact]
    public async Task A_person_with_the_author_role_but_not_the_developer_role_gets_403_and_no_request_is_stored()
    {
        using var folder = NewPackageFolder("import sys\n");
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        var requests = await CountAsync("axis.publish_requests");

        foreach (var identity in new[] { "author", "outsider" })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var response = await PostAsync(client, """{"package":"tools"}""");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            if (identity == "author")
            {
                using var problem = await ReadAsync(response);
                Assert.Equal(NoDeveloper, problem.RootElement.GetProperty("title").GetString());
            }
        }

        Assert.Equal(requests, await CountAsync("axis.publish_requests"));
    }

    [Fact]
    public async Task A_build_while_the_application_packages_switch_is_off_answers_403_and_stores_no_request()
    {
        using var folder = NewPackageFolder("import sys\n");
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: false);
        using var client = factory.CreateClient().SignedInAs("developer");
        var requests = await CountAsync("axis.publish_requests");

        using var response = await PostAsync(client, """{"package":"tools"}""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(requests, await CountAsync("axis.publish_requests"));
    }

    [Fact]
    public async Task A_body_that_is_not_exactly_a_package_string_answers_400_an_unknown_package_404_and_a_wrong_media_type_415_and_none_is_stored()
    {
        using var folder = NewPackageFolder("import sys\n");
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var requests = await CountAsync("axis.publish_requests");

        foreach (var body in new[] { "{}", """{"package":1}""", """{"package":"tools","extra":"x"}""", "[]", "not json" })
        {
            using var response = await PostAsync(client, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        foreach (var package in new[] { "other", "..", "" })
        {
            using var response = await PostAsync(client, $$"""{"package":"{{package}}"}""");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var problem = await ReadAsync(response);
            Assert.Equal("The application has no package with this id.", problem.RootElement.GetProperty("title").GetString());
        }

        using var text = await PostAsync(client, """{"package":"tools"}""", contentType: "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        Assert.Equal(requests, await CountAsync("axis.publish_requests"));
    }

    // The sample folder with the package tools: a manifest, the adapter and an empty lock file.
    private static TemporaryFolder NewPackageFolder(string adapter)
    {
        var folder = StudioPublishTests.NewFolder();
        var package = Path.Combine(folder.Path, "packages", "tools");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "axis-package.json"), McpTestPackageTests.Manifest);
        File.WriteAllText(Path.Combine(package, "adapter.py"), adapter);
        File.WriteAllText(Path.Combine(package, "requirements.txt"), McpTestPackageTests.NoDependencies);
        return folder;
    }

    // The publisher of tenant default, with application packages on and the host of the test as its application runner.
    private StudioPublishTests.RunningPublisher RunPublisher(TestPackageHost host) =>
        StudioPublishTests.RunPublisher(
            "default",
            "localhost",
            database.ConnectionString,
            extra: new Dictionary<string, string?>
            {
                ["Extensions:ApplicationPackages"] = "true",
                ["Extensions:Runners:Application"] = host.Address,
                ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
            });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BuildUrl, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue(contentType)),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    // Polls the request through the API until it is succeeded or failed.
    private static async Task<JsonDocument> WaitForFinalAsync(HttpClient client, string location)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            var status = await GetAsync(client, location);
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

    private static async Task<JsonDocument> GetAsync(HttpClient client, string location)
    {
        using var response = await client.GetAsync(new Uri(location, UriKind.RelativeOrAbsolute), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<long> CountAsync(string table) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"SELECT count(*) FROM {table}", CancellationToken))!;

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(string folder, bool switchOn)
    {
        var platform = await database.CreateDatabaseAsync("studio_package_build", CancellationToken);
        await MigrateTenantDatabaseAsync();
        var revisions = Directory.CreateTempSubdirectory("axis-studio-package-build-revisions-").FullName;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:RevisionsPath", revisions);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:DeveloperRole", "developer");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("developer", "Developer User", "author", "developer"),
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("outsider", "Outsider User", "developer"));
            if (switchOn)
            {
                // The configuration source has no switch, so the host turns it on for every tenant.
                builder.ConfigureTestServices(PackagesOnCatalog.Switch);
            }
        });
    }

    private async Task MigrateTenantDatabaseAsync()
    {
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
        }

        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(database.ConnectionString).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(database.ConnectionString, CancellationToken);
    }
}
