using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The expression check answers within the time limit on a typical application, and it reads the folder on each
/// call, so a rule edited on disk shows in the next check. The class runs alone, so no other test competes for CPU
/// with the timed checks.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class StudioExpressionCheckPerformanceTests
{
    // Each check compiles the typical application in memory. The limit is the one the author sees while typing.
    private static readonly TimeSpan _checkTime = TimeSpan.FromMilliseconds(300);

    private const int WarmUpChecks = 10;
    private const int Attempts = 3;
    private const int Checks = 100;
    private const int TypeErrorCheck = 50;
    private const string Application = "Typical";
    private const string CheckUrl = "/api/studio/apps/Typical/expressions/check";
    private const string RuleFile = "rules/rule-000.json";
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hundred_checks_of_a_typical_application_each_answer_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        // The first checks of a process run code that the JIT has not yet optimised, so they are not timed.
        for (var warmUp = 0; warmUp < WarmUpChecks; warmUp++)
        {
            using var response = await CheckAsync(client, EntityId(folder.Path, warmUp % TypicalApplication.Entities), "quantity > 0");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var slowest = TimeSpan.Zero;
        for (var n = 0; n < Checks; n++)
        {
            var index = n % TypicalApplication.Entities;
            var expression = n == TypeErrorCheck ? "quantity > 'x'" : $"quantity > {n}";

            // A shared runner can pause the process for a garbage collection or another job, so a check over the
            // limit is timed again. Only a check that stays over the limit on every attempt fails.
            var (elapsed, body) = await TimedCheckAsync(client, EntityId(folder.Path, index), expression);
            for (var attempt = 1; attempt < Attempts && elapsed >= _checkTime; attempt++)
            {
                var retry = await TimedCheckAsync(client, EntityId(folder.Path, index), expression);
                (elapsed, body) = retry.Elapsed < elapsed ? retry : (elapsed, body);
            }

            Assert.True(elapsed < _checkTime, $"Check {n} of '{expression}' took {elapsed} at best of {Attempts} attempts.");
            slowest = elapsed > slowest ? elapsed : slowest;

            using var json = JsonDocument.Parse(body);
            if (n == TypeErrorCheck)
            {
                Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("type").ValueKind);
                var diagnostic = Assert.Single(json.RootElement.GetProperty("diagnostics").EnumerateArray());
                Assert.Equal("AXC0047", diagnostic.GetProperty("code").GetString());
            }
            else
            {
                Assert.Equal("boolean", json.RootElement.GetProperty("type").GetString());
                Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"The slowest check took {slowest}.");
    }

    [Fact]
    public async Task A_rule_edited_on_disk_shows_in_the_next_check()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        var rulePath = Path.Combine(folder.Path, RuleFile);
        var ruleId = IdOf(rulePath);
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var entity = EntityId(folder.Path, 1);

        using (var before = await CheckAsync(client, entity, "Rule000(quantity)"))
        {
            using var json = JsonDocument.Parse(await before.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal("boolean", json.RootElement.GetProperty("type").GetString());
            Assert.Empty(json.RootElement.GetProperty("diagnostics").EnumerateArray());
        }

        File.WriteAllText(rulePath, $$"""
            {
              "id": "{{ruleId}}",
              "kind": "rule",
              "name": "Rule000",
              "formatVersion": 1,
              "parameters": [{ "name": "value", "type": "integer" }],
              "resultType": "integer",
              "expression": "value + 1"
            }
            """);

        using var after = await CheckAsync(client, entity, "Rule000(quantity)");
        using var answer = JsonDocument.Parse(await after.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("type").ValueKind);
        var diagnostic = Assert.Single(answer.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("AXC0048", diagnostic.GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(string folder) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"));
        });

    private static async Task<HttpResponseMessage> CheckAsync(HttpClient client, string resourceId, string expression)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(CheckUrl, UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { resourceId, path = "/validations/0/expression", expression }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<(TimeSpan Elapsed, string Body)> TimedCheckAsync(HttpClient client, string resourceId, string expression)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await CheckAsync(client, resourceId, expression);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (stopwatch.Elapsed, body);
    }

    private static string EntityId(string folder, int index) => IdOf(Path.Combine(folder, "entities", $"entity-{index:D2}.json"));

    private static string IdOf(string path)
    {
        using var file = JsonDocument.Parse(File.ReadAllBytes(path));
        return file.RootElement.GetProperty("id").GetString()!;
    }
}
