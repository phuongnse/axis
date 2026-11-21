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
/// <c>POST /api/studio/apps/{application}/build</c> builds one package of the working folder without a publish,
/// <c>POST /api/studio/apps/{application}/test</c> with <c>{ package }</c> tests it and
/// <c>POST /api/studio/apps/{application}/run</c> with <c>{ package, operation, input }</c> runs one of its operations, see
/// <c>docs/reference/studio-packages.md#the-authoring-api</c>. The server is tenant <c>default</c> on a real PostgreSQL database and
/// the publisher runs the request against the host of the test process, with a stub Trivy and the real pip, while the publisher's
/// <c>PackageBuild:Python</c> names a missing command. The test identity <c>developer</c> holds the author and developer roles and
/// <c>author</c> the author role only. The tests use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class StudioPackageBuildTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string BuildUrl = "/api/studio/apps/PurchaseRequests/build";

    private const string TestUrl = "/api/studio/apps/PurchaseRequests/test";

    private const string RunUrl = "/api/studio/apps/PurchaseRequests/run";

    private const string NoDeveloper = "You do not hold the developer role.";

    // Two tests, one printing a line and one failing at line 10, column 9.
    private const string ToolsTests = """
        import unittest


        class ToolsCase(unittest.TestCase):
            def test_passes(self):
                print("checking the sum")
                self.assertEqual(2, 1 + 1)

            def test_fails(self):
                self.assertEqual(1, 2)
        """;

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

    [Fact]
    public async Task A_test_of_a_package_ends_failed_with_each_tests_duration_and_a_failed_tests_place_and_the_output()
    {
        using var folder = NewPackageFolder("import sys\n", ToolsTests);
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });

        using var accepted = await PostAsync(client, """{"package":"tools"}""", url: TestUrl);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var queued = await ReadAsync(accepted);
        Assert.Equal("test", queued.RootElement.GetProperty("kind").GetString());
        Assert.Equal("tools", queued.RootElement.GetProperty("package").GetString());
        Assert.Empty(queued.RootElement.GetProperty("packageTests").EnumerateArray());
        Assert.Empty(queued.RootElement.GetProperty("output").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("scenarios").ValueKind);
        await using var publisher = RunPublisher(host);
        using var finished = await WaitForFinalAsync(client, accepted.Headers.Location!.OriginalString);

        var root = finished.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("tools", root.GetProperty("package").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("scenarios").ValueKind);
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
        var tests = root.GetProperty("packageTests").EnumerateArray().ToList();
        Assert.Equal(2, tests.Count);
        var passed = Assert.Single(tests, test => test.GetProperty("name").GetString() == "test_tools.ToolsCase.test_passes");
        Assert.Equal("passed", passed.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, passed.GetProperty("message").ValueKind);
        Assert.True(passed.GetProperty("durationMs").GetInt64() >= 0);
        Assert.False(passed.TryGetProperty("file", out _));
        var failed = Assert.Single(tests, test => test.GetProperty("name").GetString() == "test_tools.ToolsCase.test_fails");
        Assert.Equal("failed", failed.GetProperty("outcome").GetString());
        Assert.StartsWith("AssertionError", failed.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(failed.GetProperty("durationMs").GetInt64() >= 0);
        Assert.Equal("test_tools.py", failed.GetProperty("file").GetString());
        Assert.Equal(10, failed.GetProperty("line").GetInt32());
        Assert.Equal(9, failed.GetProperty("column").GetInt32());
        Assert.Contains(root.GetProperty("output").EnumerateArray(), line => line.GetString()!.Contains("checking the sum", StringComparison.Ordinal));
        Assert.Equal([("tools", "1.0.0")], host.Tests());
    }

    [Fact]
    public async Task A_test_of_a_package_needs_the_developer_role_and_the_switch_and_a_scenario_test_needs_the_author_role_only()
    {
        using var folder = NewPackageFolder("import sys\n", ToolsTests);
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        await using var off = await CreateFactoryAsync(folder.Path, switchOn: false);
        var requests = await CountAsync("axis.publish_requests");

        foreach (var identity in new[] { "author", "outsider" })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var response = await PostAsync(client, """{"package":"tools"}""", url: TestUrl);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            if (identity == "author")
            {
                using var problem = await ReadAsync(response);
                Assert.Equal(NoDeveloper, problem.RootElement.GetProperty("title").GetString());
            }
        }

        using (var developer = off.CreateClient().SignedInAs("developer"))
        {
            using var response = await PostAsync(developer, """{"package":"tools"}""", url: TestUrl);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(requests, await CountAsync("axis.publish_requests"));

        using var author = factory.CreateClient().SignedInAs("author");
        using var scenarios = await PostAsync(author, "{}", url: TestUrl);
        Assert.Equal(HttpStatusCode.Accepted, scenarios.StatusCode);
        using var queued = await ReadAsync(scenarios);
        Assert.Equal("test", queued.RootElement.GetProperty("kind").GetString());
        Assert.Empty(queued.RootElement.GetProperty("scenarios").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("package").ValueKind);
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("packageTests").ValueKind);
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("output").ValueKind);
    }

    [Fact]
    public async Task A_test_body_with_a_package_and_scenarios_or_a_package_that_is_not_a_string_answers_400_an_unknown_package_404_and_none_is_stored()
    {
        using var folder = NewPackageFolder("import sys\n", ToolsTests);
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var requests = await CountAsync("axis.publish_requests");

        foreach (var body in new[] { """{"package":"tools","scenarios":[]}""", """{"package":1}""", """{"package":"tools","extra":"x"}""", """{"scenarios":["a"],"package":"tools"}""" })
        {
            using var response = await PostAsync(client, body, url: TestUrl);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        foreach (var package in new[] { "other", "..", "" })
        {
            using var response = await PostAsync(client, $$"""{"package":"{{package}}"}""", url: TestUrl);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using var problem = await ReadAsync(response);
            Assert.Equal("The application has no package with this id.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(requests, await CountAsync("axis.publish_requests"));
    }

    [Fact]
    public async Task A_run_of_the_starters_echo_answers_202_and_ends_succeeded_with_the_echoed_output_a_duration_and_the_hosts_lines()
    {
        using var folder = NewStarterFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });

        using var accepted = await PostAsync(client, """{"package":"tools","operation":"echo","input":{"text":"hello"}}""", url: RunUrl);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var queued = await ReadAsync(accepted);
        Assert.Equal("run", queued.RootElement.GetProperty("kind").GetString());
        Assert.Equal("queued", queued.RootElement.GetProperty("status").GetString());
        Assert.Equal("tools", queued.RootElement.GetProperty("package").GetString());
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("run").ValueKind);
        Assert.Empty(queued.RootElement.GetProperty("output").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("scenarios").ValueKind);
        Assert.Equal(JsonValueKind.Null, queued.RootElement.GetProperty("packageTests").ValueKind);
        await using var publisher = RunPublisher(host, proxy: true);
        using var finished = await WaitForFinalAsync(client, accepted.Headers.Location!.OriginalString);

        var root = finished.RootElement;
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal("run", root.GetProperty("kind").GetString());
        var run = root.GetProperty("run");
        Assert.Equal("echo", run.GetProperty("operation").GetString());
        Assert.Equal("succeeded", run.GetProperty("outcome").GetString());
        Assert.Equal("hello", run.GetProperty("output").GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("error").ValueKind);
        Assert.True(run.GetProperty("durationMs").GetInt64() >= 0);
        Assert.NotEmpty(root.GetProperty("output").EnumerateArray());
    }

    [Fact]
    public async Task A_run_needs_the_developer_role_and_the_switch_and_a_denied_run_stores_no_request()
    {
        using var folder = NewStarterFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        await using var off = await CreateFactoryAsync(folder.Path, switchOn: false);
        var requests = await CountAsync("axis.publish_requests");
        const string body = """{"package":"tools","operation":"echo","input":{}}""";

        foreach (var identity in new[] { "author", "outsider" })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var response = await PostAsync(client, body, url: RunUrl);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            if (identity == "author")
            {
                using var problem = await ReadAsync(response);
                Assert.Equal(NoDeveloper, problem.RootElement.GetProperty("title").GetString());
            }
        }

        using (var developer = off.CreateClient().SignedInAs("developer"))
        {
            using var response = await PostAsync(developer, body, url: RunUrl);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(requests, await CountAsync("axis.publish_requests"));
    }

    [Fact]
    public async Task A_run_body_that_is_not_exactly_a_package_an_operation_and_an_input_object_answers_400_another_media_type_415_an_unknown_package_404_and_none_is_stored()
    {
        using var folder = NewStarterFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var requests = await CountAsync("axis.publish_requests");

        foreach (var body in new[]
        {
            """{"package":"tools","input":{}}""",
            """{"package":"tools","operation":"echo"}""",
            """{"operation":"echo","input":{}}""",
            """{"package":"tools","operation":"echo","input":[]}""",
            """{"package":"tools","operation":"echo","input":"x"}""",
            """{"package":"tools","operation":"echo","input":null}""",
            """{"package":"tools","operation":1,"input":{}}""",
            """{"package":1,"operation":"echo","input":{}}""",
            """{"package":"tools","operation":"echo","input":{},"extra":"x"}""",
            """["tools"]""",
            "not json",
        })
        {
            using var response = await PostAsync(client, body, url: RunUrl);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var text = await PostAsync(client, """{"package":"tools","operation":"echo","input":{}}""", contentType: "text/plain", url: RunUrl);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);

        using var other = await PostAsync(client, """{"package":"other","operation":"echo","input":{}}""", url: RunUrl);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var problem = await ReadAsync(other);
        Assert.Equal("The application has no package with this id.", problem.RootElement.GetProperty("title").GetString());

        Assert.Equal(requests, await CountAsync("axis.publish_requests"));
    }

    // The sample folder with the Python starter as the package tools.
    private static TemporaryFolder NewStarterFolder()
    {
        var folder = StudioPublishTests.NewFolder();
        var package = Path.Combine(folder.Path, "packages", "tools");
        foreach (var (name, text) in PublisherRunRequestTests.StarterFiles())
        {
            var path = Path.Combine(package, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return folder;
    }

    // The sample folder with the package tools: a manifest, the adapter, an empty lock file and, when given, test_tools.py.
    private static TemporaryFolder NewPackageFolder(string adapter, string? tests = null)
    {
        var folder = StudioPublishTests.NewFolder();
        var package = Path.Combine(folder.Path, "packages", "tools");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "axis-package.json"), McpTestPackageTests.Manifest);
        File.WriteAllText(Path.Combine(package, "adapter.py"), adapter);
        File.WriteAllText(Path.Combine(package, "requirements.txt"), McpTestPackageTests.NoDependencies);
        if (tests is not null)
        {
            File.WriteAllText(Path.Combine(package, "test_tools.py"), tests);
        }

        return folder;
    }

    // The publisher of tenant default, with application packages on and the host of the test as its application runner.
    // A run also needs a free port for the publisher's egress proxy, which proxy asks for.
    private StudioPublishTests.RunningPublisher RunPublisher(TestPackageHost host, bool proxy = false)
    {
        var extra = new Dictionary<string, string?>
        {
            ["Extensions:ApplicationPackages"] = "true",
            ["Extensions:Runners:Application"] = host.Address,
            ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
        };
        if (proxy)
        {
            extra["Extensions:Runners:ProxyPort"] = "0";
        }

        return StudioPublishTests.RunPublisher("default", "localhost", database.ConnectionString, extra: extra);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, string contentType = "application/json", string url = BuildUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative))
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
        await MigrateTenantDatabaseAsync(database.ConnectionString);
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

    // Migrates the three contexts of a tenant database and grants the runtime role, which the publisher of tenant default needs before it serves it.
    internal static async Task MigrateTenantDatabaseAsync(string connectionString)
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
        }

        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await processes.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
    }
}
