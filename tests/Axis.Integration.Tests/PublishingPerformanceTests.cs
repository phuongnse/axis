using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Server.Authentication;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The M10 features stay fast at realistic sizes: a publish request of a typical application against
/// <c>axis-admin activate</c> on the same folder, the release history route with 500 activations, 200 Studio patches
/// through two server instances, and 1,000 session checks against the shared key ring. All tests are in one class, so
/// xUnit runs them one after another and they do not compete for CPU.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class PublishingPerformanceTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    // The publisher looks for new requests once a second, so a request waits up to a second before it is claimed.
    // The margin covers that wait and the polling of the test. The publish does the same work as activate on a
    // tenant that has no release yet, so any more than the margin points to work that only the publish repeats.
    // Locally, activate took 1.6 s and the publish 1.4 s.
    private static readonly TimeSpan _publishMargin = TimeSpan.FromSeconds(5);

    // The test gives up on a publish that is not final after this long, so a stuck publisher fails the test.
    private static readonly TimeSpan _publishDeadline = TimeSpan.FromSeconds(120);

    // The route compiles the folder for folderContentHash on every call, and reads the 500 rows. The limit is the
    // one second that Studio may take to show the history. It is still tight, so the table read has to stay one indexed query. Locally, the
    // answer with 500 rows took 0.04 s.
    private static readonly TimeSpan _historyTime = TimeSpan.FromSeconds(1);

    // Each patch compiles the folder of 201 small files and writes one file under the lock of the folder, so the
    // time includes the compile. Locally, the 200 patches took 3.9 s. The limit is about 2.5 times that. It still
    // catches a lock that is held across requests or a wait that adds seconds to each write.
    private static readonly TimeSpan _patchTime = TimeSpan.FromSeconds(10);

    // Reading the session cookie needs the key from the key ring, which the server caches after the first
    // read. 1,000 requests are far below the limit, so a request that reads the platform database each time,
    // as a key ring that is not cached would, fails the test. Locally, the 1,000 requests took 0.24 s.
    private static readonly TimeSpan _meTime = TimeSpan.FromSeconds(10);

    private static readonly string[] _steps = ["version", "compile", "activate", "seed"];

    private const int Activations = 500;
    private const int Patches = 200;
    private const int Requests = 1_000;

    // Each waiting patch holds a platform database connection, and a test class has about 30 of them. Eight at a
    // time, four per instance, keep the pool far from empty, so the test measures the lock and not the pool.
    private const int PatchesInFlight = 8;
    private const int RequestsInFlight = 16;

    private const string HistoryTenant = "a";
    private const string HistoryHost = "a.example.test";
    private const string ReleasesUrl = "/api/studio/apps/PurchaseRequests/releases";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_publish_request_of_a_typical_application_succeeds_within_the_time_of_activate_plus_the_margin()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        string[] ids = [scenario.NewTenantId(), scenario.NewTenantId()];
        foreach (var id in ids)
        {
            Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        }

        var applicationId = Guid.NewGuid();
        using var folder = TypicalApplication.Write(applicationId, withErrors: false, out _);

        // The first compile of a process pays for the JIT, which neither measurement should include.
        ApplicationCompiler.Compile(folder.Path);

        // Both tenants build the same tables from nothing. Activate on a tenant that holds the release would do nothing.
        var stopwatch = Stopwatch.StartNew();
        var activate = await scenario.RunAsync(["activate", folder.Path, "--tenant", ids[0]]);
        stopwatch.Stop();
        var activateElapsed = stopwatch.Elapsed;
        TestContext.Current.TestOutputHelper?.WriteLine($"Activate took {activateElapsed}.");
        Assert.Equal(0, activate.Exit);
        Assert.Empty(activate.Error);
        Assert.Equal(_steps.Select(step => $"{ids[0]} {step} ok"), activate.Output);

        var snapshot = await SnapshotAsync(folder.Path);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var publisher = scenario.RunAsync(["publisher"], stop: stop, extra: new Dictionary<string, string?> { ["Tenancy:Source"] = "directory" });
        TimeSpan publishElapsed;
        Guid request;
        try
        {
            request = await QueueAsync(scenario, ids[1], applicationId, snapshot);
            stopwatch.Restart();
            string? status;
            while (true)
            {
                status = (string?)await ScalarAsync(scenario, ids[1], $"SELECT status FROM axis.publish_requests WHERE id = '{request}'");
                publishElapsed = stopwatch.Elapsed;
                if (status is "succeeded" or "failed")
                {
                    break;
                }

                Assert.False(publisher.IsCompleted, "The publisher stopped before the request was final.");
                Assert.True(publishElapsed < _publishDeadline, $"The request was still {status} after {publishElapsed}.");
                await Task.Delay(100, CancellationToken);
            }

            Assert.Equal("succeeded", status);
        }
        finally
        {
            await stop.CancelAsync();
        }

        Assert.Equal(0, (await publisher).Exit);
        TestContext.Current.TestOutputHelper?.WriteLine($"The publish took {publishElapsed}.");
        var release = await ScalarAsync(scenario, ids[1], $"SELECT release_id FROM axis.publish_requests WHERE id = '{request}'");
        Assert.NotNull(release);
        Assert.Equal(release, await ScalarAsync(scenario, ids[1], "SELECT release_id FROM axis.active_releases"));
        Assert.True(
            publishElapsed <= activateElapsed + _publishMargin,
            $"The publish took {publishElapsed}, and activate took {activateElapsed}.");
    }

    [Fact]
    public async Task The_release_history_route_answers_500_activations_within_the_time_limit()
    {
        using var folder = StudioPublishTests.NewFolder();
        var connectionString = await database.CreateDatabaseAsync("studio_history", CancellationToken);
        await using var factory = CreateHistoryFactory(connectionString, folder.Path);

        // Creating a client starts the server, which activates the folder as release 1.
        using var author = factory.CreateClient().SignedInAs("hoa");
        var textsPath = Path.Combine(folder.Path, "texts", "en.json");
        var texts = await File.ReadAllTextAsync(textsPath, CancellationToken);
        Assert.Contains("\"Departments\"", texts, StringComparison.Ordinal);
        await File.WriteAllTextAsync(textsPath, texts.Replace("\"Departments\"", "\"Teams\"", StringComparison.Ordinal), CancellationToken);
        await StudioReleaseHistoryTests.ActivateAsync(connectionString, folder.Path, ActivationActor.User("hoa", "Hoa Author", "Spring", "Teams rename", Guid.NewGuid()));

        // The rows alternate between the two releases, as repeated rollbacks leave them. Each release keeps its number.
        await ScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.release_activations (id, release_id, application_id, number, content_hash, activated_at, actor, label, note)
            SELECT uuidv7(), r.release_id, r.application_id, r.number, r.content_hash,
                   (SELECT max(activated_at) FROM axis.release_activations) - g * interval '1 minute',
                   'hoa', 'Label ' || g, repeat('x', 200)
            FROM generate_series(1, {Activations - 2}) g
            JOIN axis.release_activations r ON r.number = CASE WHEN g % 2 = 0 THEN 2 ELSE 1 END
            """);
        Assert.Equal((long)Activations, await ScalarAsync(connectionString, "SELECT count(*) FROM axis.release_activations"));

        var (status, body, elapsed) = await AnswerAsync(author, ReleasesUrl);

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(Activations, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, json.RootElement.GetProperty("active").GetProperty("number").GetInt32());
        Assert.True(elapsed < _historyTime, $"The release history took {elapsed}.");
    }

    [Fact]
    public async Task Two_hundred_patches_to_different_files_on_two_instances_all_answer_200_within_the_time_limit()
    {
        using var folder = new TemporaryFolder().With(
            "application.json",
            JsonSerializer.Serialize(new { id = Guid.NewGuid(), kind = "application", name = "Rules", formatVersion = 1 }));
        var files = new List<(string Id, string Path)>();
        for (var k = 0; k < Patches; k++)
        {
            var id = Guid.NewGuid().ToString();
            var relative = $"rules/rule-{k:D3}.json";
            folder.With(
                relative,
                JsonSerializer.Serialize(new
                {
                    id,
                    kind = "rule",
                    name = $"Rule{k:D3}",
                    formatVersion = 1,
                    parameters = new[] { new { name = "value", type = "integer" } },
                    resultType = "boolean",
                    expression = $"value > {k}",
                }));
            files.Add((id, Path.Combine(folder.Path, relative)));
        }

        var platform = await database.CreateDatabaseAsync("studio_patches", CancellationToken);

        // The instances start one after the other, because both run the platform migrations.
        await using var first = CreateStudioFactory(platform, folder.Path);
        using var firstClient = first.CreateClient().SignedInAs("hoa");
        await using var second = CreateStudioFactory(platform, folder.Path);
        using var secondClient = second.CreateClient().SignedInAs("hoa");
        HttpClient[] clients = [firstClient, secondClient];

        var versions = files.Select(file => Sha256(file.Path)).ToArray();
        var statuses = new ConcurrentDictionary<int, HttpStatusCode>();
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(clients.Select((client, instance) => Parallel.ForEachAsync(
            Enumerable.Range(0, Patches).Where(k => k % 2 == instance),
            new ParallelOptions { MaxDegreeOfParallelism = PatchesInFlight / 2, CancellationToken = CancellationToken },
            async (k, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/studio/apps/Rules/resources/{files[k].Id}", UriKind.Relative))
                {
                    Content = new StringContent(
                        $$"""[{"op":"replace","path":"/expression","value":"value > {{k + 1000}}"}]""",
                        Encoding.UTF8,
                        "application/json-patch+json"),
                };
                request.Headers.Add("X-Axis-Csrf", "1");
                request.Headers.TryAddWithoutValidation("If-Match", $"\"{versions[k]}\"");
                using var response = await client.SendAsync(request, token);
                statuses[k] = response.StatusCode;
            })));
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"{Patches} patches took {stopwatch.Elapsed}.");
        Assert.Equal(Patches, statuses.Count);
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status.Value));
        Assert.True(stopwatch.Elapsed < _patchTime, $"{Patches} patches took {stopwatch.Elapsed}.");
        for (var k = 0; k < Patches; k++)
        {
            Assert.Contains($"\"value > {k + 1000}\"", await File.ReadAllTextAsync(files[k].Path, CancellationToken), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task One_thousand_me_requests_on_two_instances_with_the_shared_key_ring_all_answer_200_within_the_time_limit()
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(database.ConnectionString, CancellationToken);

        // The instances start one after the other, because both run the platform migrations.
        await using var first = CreateSessionFactory(platform);
        var cookie = await SharedKeyRingTests.SignInAsync(first);
        await using var second = CreateSessionFactory(platform);
        using var firstClient = first.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var secondClient = second.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        HttpClient[] clients = [firstClient, secondClient];

        var statuses = new ConcurrentDictionary<int, HttpStatusCode>();
        var stopwatch = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Requests),
            new ParallelOptions { MaxDegreeOfParallelism = RequestsInFlight, CancellationToken = CancellationToken },
            async (i, token) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
                request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
                using var response = await clients[i % clients.Length].SendAsync(request, token);
                statuses[i] = response.StatusCode;
            });
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"{Requests} requests took {stopwatch.Elapsed}.");
        Assert.Equal(Requests, statuses.Count);
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status.Value));
        Assert.True(stopwatch.Elapsed < _meTime, $"{Requests} requests took {stopwatch.Elapsed}.");
    }

    // Sends the request once to warm up, then times the next answer, body included.
    private static async Task<(HttpStatusCode Status, string Body, TimeSpan Elapsed)> AnswerAsync(HttpClient client, string url)
    {
        using (var warmUp = await client.SendAsync(RecordApiFixture.Request(url, HistoryHost), CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, warmUp.StatusCode);
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.SendAsync(RecordApiFixture.Request(url, HistoryHost), CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"The release history took {stopwatch.Elapsed}.");
        return (response.StatusCode, body, stopwatch.Elapsed);
    }

    // Every JSON file of the folder, as the publish route stores it.
    private static async Task<string> SnapshotAsync(string folder)
    {
        var items = new JsonArray();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(path, CancellationToken);
            items.Add(new JsonObject
            {
                ["file"] = Path.GetRelativePath(folder, path).Replace('\\', '/'),
                ["version"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                ["content"] = Encoding.UTF8.GetString(bytes),
            });
        }

        return items.ToJsonString();
    }

    // Stores a request over the runtime role, as the server will.
    private static async Task<Guid> QueueAsync(AdminScenario scenario, string tenant, Guid applicationId, string snapshot)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.RuntimeUser, password));
        await connection.OpenAsync(CancellationToken);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.publish_requests (id, application_id, kind, status, snapshot, requested_by, requested_by_name, requested_at)
            VALUES (@id, @application, 'publish', 'queued', @snapshot, 'hoa', 'Hoa Author', clock_timestamp())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = snapshot });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static Task<object?> ScalarAsync(AdminScenario scenario, string id, string sql) =>
        ScalarAsync(new NpgsqlConnectionStringBuilder(scenario.Platform) { Database = $"axis_{id}" }.ConnectionString, sql);

    private static Task<object?> ScalarAsync(string connectionString, string sql) =>
        PostgreSqlServer.AdminScalarAsync(connectionString, sql, CancellationToken);

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static WebApplicationFactory<Program> CreateHistoryFactory(string connectionString, string folder) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting($"Tenants:{HistoryTenant}:Hosts:0", HistoryHost);
            builder.UseSetting($"Tenants:{HistoryTenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting($"Tenants:{HistoryTenant}:SchemaConnectionString", connectionString);
            builder.UseTestOidc(HistoryTenant);
            builder.UseSetting($"Tenants:{HistoryTenant}:Bindings:PurchasingSystemUrl:Kind", "url");
            builder.UseSetting($"Tenants:{HistoryTenant}:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
            builder.UseSetting($"Tenants:{HistoryTenant}:Bindings:PurchasingSystemToken:Kind", "secret");
            builder.UseSetting($"Tenants:{HistoryTenant}:Bindings:PurchasingSystemToken:Value", "studio-test-token");
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("ActivateOnStartup:0", folder);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(new TestIdentity("hoa", "Hoa Author", "author"));
        });

    // One of two instances on a platform database. The tenant is the class database.
    private WebApplicationFactory<Program> CreateStudioFactory(string platform, string folder) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(new TestIdentity("hoa", "Hoa Author", "author"));
        });

    private WebApplicationFactory<Program> CreateSessionFactory(string platform) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseTestOidc("default");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SharedKeyRingTests.SessionStartupFilter>());
        });
}
