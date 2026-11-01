using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The M11 Studio features stay fast at realistic sizes: the names of a typical application, 20 rule patches in one
/// <c>changes</c> call, an application created from a template of 300 files, and a <c>test</c> request of 20
/// scenarios run by a real publisher. All tests are in one class, so xUnit runs them one after another and they do
/// not compete for CPU. The class runs after every parallel collection, so <see cref="PublisherTestRequestTests"/>,
/// which drops every scenario database before each test, cannot drop the databases of this class's run.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class StudioAuthoringPerformanceTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    // The names answer compiles the typical application in memory. Locally, it took 0.22 s.
    private static readonly TimeSpan _namesTime = TimeSpan.FromSeconds(2);

    // The changes call compiles the folder once and writes 20 files. Locally, it took 0.35 s.
    private static readonly TimeSpan _changesTime = TimeSpan.FromSeconds(2);

    // The create route compiles the 300 copied files in memory and writes them. Locally, it took 0.11 s.
    private static readonly TimeSpan _createTime = TimeSpan.FromSeconds(5);

    // A test request runs 20 scenarios, and each creates, migrates and drops a database. Locally, it took 22 s, which
    // includes the publisher's one-second poll for the request. The test gives up after the deadline.
    private static readonly TimeSpan _testTime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _testDeadline = TimeSpan.FromMinutes(3);

    private const int Patches = 20;
    private const int Scenarios = 20;
    private const int TemplateFiles = 300;
    private const string Application = "Typical";
    private const string TemplateId = "typical";
    private const string TemplateVersion = "1.0.0";
    private const string CreatedFolder = "created";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_names_of_a_typical_application_answer_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        var (status, body, elapsed) = await Answer(
            "The names",
            () => client.GetAsync(new Uri($"/api/studio/apps/{Application}/names", UriKind.Relative), CancellationToken));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(TypicalApplication.Entities, json.RootElement.GetProperty("entities").GetArrayLength());
        Assert.Equal(TypicalApplication.DataSources, json.RootElement.GetProperty("dataSources").GetArrayLength());
        Assert.Equal(TypicalApplication.Rules, json.RootElement.GetProperty("rules").GetArrayLength());
        Assert.True(elapsed < _namesTime, $"The names took {elapsed}.");
    }

    [Fact]
    public async Task A_changes_call_patching_20_rules_of_a_typical_application_answers_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        var rules = Enumerable.Range(0, Patches)
            .Select(k => Path.Combine(folder.Path, $"rules/rule-{k:D3}.json"))
            .ToArray();
        var ids = rules.Select(IdOf).ToArray();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        // The versions are read for each send, so the timed call carries the versions that the warm-up call left.
        var (status, body, elapsed) = await Answer(
            "The changes",
            () => ChangesAsync(client, ids, rules));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(Patches, items.Count);
        for (var k = 0; k < Patches; k++)
        {
            Assert.Equal(ids[k], items[k].GetProperty("id").GetString());
        }

        Assert.True(elapsed < _changesTime, $"The changes took {elapsed}.");
    }

    [Fact]
    public async Task Creating_an_application_from_a_template_of_300_files_answers_201_within_the_time_limit()
    {
        using var source = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        using var templates = new TemporaryFolder();
        using var applications = new TemporaryFolder();
        using var studio = new TemporaryFolder();
        Assert.Equal(TemplateFiles, WriteTemplate(source.Path, templates.Path));
        await using var factory = await CreateFactoryAsync(studio.Path, applications.Path, templates.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        // The first create pays for the JIT and the first compile of the process.
        using (var warmUp = await CreateAsync(client, $$"""{ "name": "WarmUp", "template": "{{TemplateId}}" }"""))
        {
            Assert.Equal(HttpStatusCode.Created, warmUp.StatusCode);
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await CreateAsync(client, $$"""{ "name": "Created", "template": "{{TemplateId}}" }""");
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"The create took {stopwatch.Elapsed}.");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.DoesNotContain(json.RootElement.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("severity").GetString() == "error");
        Assert.Equal(TemplateFiles, Directory.EnumerateFiles(Path.Combine(applications.Path, CreatedFolder), "*.json", SearchOption.AllDirectories).Count());
        Assert.True(stopwatch.Elapsed < _createTime, $"The create took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task A_test_request_of_20_scenarios_through_a_real_publisher_succeeds_within_the_time_limit()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await PublisherTestRequestTests.CreateTenantAsync(scenario);
        var (snapshot, names) = await SnapshotAsync();
        // The request names its 20 scenarios, so the modules' scenarios, which a request without names also runs, do not count.
        var request = await QueueAsync(scenario, tenant, snapshot, JsonSerializer.Serialize(names));
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["Scenarios:ServerConnectionString"] = await PostgreSqlServer.ScenarioConnectionStringAsync(),
        };

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var stopwatch = Stopwatch.StartNew();
        var publisher = scenario.RunAsync(["publisher"], stop: stop, extra: settings);
        string? status;
        TimeSpan elapsed;
        try
        {
            while (true)
            {
                status = (string?)await ScalarAsync(scenario, tenant, $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
                elapsed = stopwatch.Elapsed;
                if (status is "succeeded" or "failed")
                {
                    break;
                }

                Assert.False(publisher.IsCompleted, "The publisher stopped before the request was final.");
                Assert.True(elapsed < _testDeadline, $"The request was still {status} after {elapsed}.");
                await Task.Delay(200, CancellationToken);
            }
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await publisher;
        TestContext.Current.TestOutputHelper?.WriteLine($"The test request took {elapsed}.");
        Assert.True(status == "succeeded", string.Join('\n', result.Output));
        Assert.Equal(0, result.Exit);
        var results = JsonNode.Parse((string)(await ScalarAsync(scenario, tenant, $"SELECT scenario_results::text FROM axis.publish_requests WHERE id = '{request}'"))!)!.AsArray();
        Assert.Equal(names.Order(StringComparer.Ordinal), results.Select(item => item!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.All(results, item => Assert.Equal("passed", item!["outcome"]!.GetValue<string>()));
        Assert.True(elapsed < _testTime, $"The test request took {elapsed}.");
    }

    // Sends the request once to warm up, then times the next answer, body included.
    private static async Task<(HttpStatusCode Status, string Body, TimeSpan Elapsed)> Answer(string name, Func<Task<HttpResponseMessage>> send)
    {
        (await send()).Dispose();

        var stopwatch = Stopwatch.StartNew();
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{name} took {stopwatch.Elapsed}.");
        return (response.StatusCode, body, stopwatch.Elapsed);
    }

    private static async Task<HttpResponseMessage> ChangesAsync(HttpClient client, string[] ids, string[] paths)
    {
        var changes = new JsonArray();
        for (var k = 0; k < Patches; k++)
        {
            changes.Add(new JsonObject
            {
                ["patch"] = new JsonArray(new JsonObject { ["op"] = "replace", ["path"] = "/expression", ["value"] = $"value > {k + 1000}" }),
                ["id"] = ids[k],
                ["version"] = Sha256(paths[k]),
            });
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/studio/apps/{Application}/changes", UriKind.Relative))
        {
            Content = new StringContent(new JsonObject { ["changes"] = changes }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/studio/apps", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    // Copies a template of 300 files from the typical application: the manifest, both texts, 30 entities, 177 rules and
    // 87 data sources, plus the site, its identity provider and its page that a template must hold. Each data source reads Entity(d % 89 + 1),
    // so the 87 kept read Entity01 to Entity29, and the rules and entities kept reference only lower or kept resources.
    private static int WriteTemplate(string source, string templates)
    {
        var folder = Path.Combine(templates, TemplateId, TemplateVersion);
        var files = new List<string> { "application.json", "texts/en.json", "texts/vi.json" };
        files.AddRange(Enumerable.Range(0, 30).Select(i => $"entities/entity-{i:D2}.json"));
        files.AddRange(Enumerable.Range(0, 177).Select(k => $"rules/rule-{k:D3}.json"));
        files.AddRange(Enumerable.Range(0, TypicalApplication.DataSources)
            .Where(d => d % 89 <= 28)
            .Select(d => $"data-sources/data-source-{d:D3}.json"));
        foreach (var file in files)
        {
            var target = Path.Combine(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(source, file), target);
        }

        Directory.CreateDirectory(Path.Combine(folder, "sites"));
        File.WriteAllText(
            Path.Combine(folder, "sites", "typical.json"),
            $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "site",
              "name": "Typical",
              "formatVersion": 1,
              "path": "typical",
              "title": { "textKey": "common.name" },
              "locales": { "default": "en", "fallback": "en", "available": ["en", "vi"] },
              "navigation": [{ "page": "Home", "label": { "textKey": "common.name" } }]
            }
            """);
        files.Add("sites/typical.json");

        Directory.CreateDirectory(Path.Combine(folder, "identity-providers"));
        File.WriteAllText(
            Path.Combine(folder, "identity-providers", "accounts.json"),
            $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }
            """);
        files.Add("identity-providers/accounts.json");

        Directory.CreateDirectory(Path.Combine(folder, "pages"));
        File.WriteAllText(
            Path.Combine(folder, "pages", "home.json"),
            $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "page",
              "name": "Home",
              "formatVersion": 1,
              "title": { "textKey": "common.name" },
              "widgets": [{ "type": "taskInbox" }]
            }
            """);
        files.Add("pages/home.json");

        File.WriteAllText(
            Path.Combine(folder, "template.json"),
            $$"""
            {
              "kind": "applicationTemplate",
              "id": "{{TemplateId}}",
              "version": "{{TemplateVersion}}",
              "title": { "en": "Typical application", "vi": "Ứng dụng điển hình" },
              "description": { "en": "A typical application.", "vi": "Một ứng dụng điển hình." }
            }
            """);
        return files.Count;
    }

    // The sample's scenarios, filled up to 20 by copying them in turn, each copy with a new id and name. Every file is versioned as the publish route stores it.
    private static async Task<(string Snapshot, List<string> Names)> SnapshotAsync()
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(_sample, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            items.Add(Item(Path.GetRelativePath(_sample, path).Replace('\\', '/'), bytes));
        }

        var originals = Directory.EnumerateFiles(Path.Combine(_sample, "scenarios"), "*.json").Order(StringComparer.Ordinal).ToList();
        Assert.True(originals.Count <= Scenarios, $"The sample holds {originals.Count} scenarios, more than the {Scenarios} of the test.");
        var names = new List<string>();
        foreach (var path in originals)
        {
            names.Add(JsonNode.Parse(await File.ReadAllTextAsync(path, CancellationToken))!["name"]!.GetValue<string>());
        }

        for (var k = 0; k < Scenarios - originals.Count; k++)
        {
            var path = originals[k % originals.Count];
            var n = (k / originals.Count) + 1;
            var original = JsonNode.Parse(await File.ReadAllTextAsync(path, CancellationToken))!.AsObject();
            var name = original["name"]!.GetValue<string>();
            var stem = Path.GetFileNameWithoutExtension(path);
            var copy = JsonNode.Parse(original.ToJsonString())!.AsObject();
            copy["id"] = Guid.NewGuid().ToString();
            copy["name"] = $"{name}Copy{n}";
            names.Add($"{name}Copy{n}");
            items.Add(Item($"scenarios/{stem}-copy-{n}.json", Encoding.UTF8.GetBytes(copy.ToJsonString())));
        }

        Assert.Equal(Scenarios, names.Count);
        return (items.ToJsonString(), names);
    }

    private static JsonObject Item(string file, byte[] bytes) =>
        new()
        {
            ["file"] = file,
            ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["content"] = Encoding.UTF8.GetString(bytes),
        };

    // Stores a test request over the runtime role, as the server will.
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, string snapshot, string scenarioNames)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, target, status, snapshot, requested_by, requested_by_name, requested_at, scenario_names)
            VALUES (@id, @application, 'test', 'tenant', 'queued', @snapshot, 'author', 'Author User', clock_timestamp(), @scenarios)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", Guid.Parse(await ApplicationIdAsync()));
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = snapshot });
        command.Parameters.Add(new NpgsqlParameter("scenarios", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = scenarioNames });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<string> ApplicationIdAsync()
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_sample, "application.json"), CancellationToken));
        return manifest.RootElement.GetProperty("id").GetString()!;
    }

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        PostgreSqlServer.AdminScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql, CancellationToken);

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(string folder, string? applicationsPath = null, string? templatesPath = null)
    {
        var platform = await database.CreateDatabaseAsync("studio_perf", CancellationToken);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            if (applicationsPath is not null)
            {
                builder.UseSetting("Studio:ApplicationsPath", applicationsPath);
            }

            if (templatesPath is not null)
            {
                builder.UseSetting("Templates:Path", templatesPath);
            }

            builder.UseTestOidc("default");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"));
        });
    }

    private static string IdOf(string path)
    {
        using var file = JsonDocument.Parse(File.ReadAllBytes(path));
        return file.RootElement.GetProperty("id").GetString()!;
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
