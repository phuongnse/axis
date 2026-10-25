using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The draft page metadata route answers a dashboard within the time limit on a typical application. Each timed post
/// changes the kind of its chart, so every answer comes from a fresh compile of the draft. The class runs alone, so no
/// other test competes for CPU with the timed posts.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class StudioPageMetadataPerformanceTests
{
    // The limit agreed in D65 for the draft page metadata route.
    private static readonly TimeSpan _postTime = TimeSpan.FromMilliseconds(500);

    private const int WarmUpPosts = 10;
    private const int Attempts = 3;
    private const int Posts = 30;
    private const string PageId = "6b2301b3-b24a-41a2-b6df-f086b01972fe";
    private const string PageUrl = $"/api/studio/apps/Typical/resources/{PageId}/page-metadata";
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private static readonly string[] _chartKinds = ["bar", "line", "pie"];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Thirty_posts_of_a_dashboard_on_a_typical_application_each_answer_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        AddDashboard(folder);
        var dashboard = JsonNode.Parse(File.ReadAllText(Path.Combine(folder.Path, "pages", "dashboard.json")))!;
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("hoa");

        // The first posts of a process run code that the JIT has not yet optimised, so they are not timed.
        for (var warmUp = 0; warmUp < WarmUpPosts; warmUp++)
        {
            using var response = await PostAsync(client, Draft(dashboard, _chartKinds[warmUp % _chartKinds.Length]));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var slowest = TimeSpan.Zero;
        for (var n = 0; n < Posts; n++)
        {
            var kind = _chartKinds[n % _chartKinds.Length];
            var draft = Draft(dashboard, kind);

            // A shared runner can pause the process for a garbage collection or another job, so a post over the
            // limit is timed again. Only a post that stays over the limit on every attempt fails.
            var (elapsed, body) = await TimedPostAsync(client, draft);
            for (var attempt = 1; attempt < Attempts && elapsed >= _postTime; attempt++)
            {
                var retry = await TimedPostAsync(client, draft);
                (elapsed, body) = retry.Elapsed < elapsed ? retry : (elapsed, body);
            }

            Assert.True(elapsed < _postTime, $"Post {n} took {elapsed} at best of {Attempts} attempts.");
            slowest = elapsed > slowest ? elapsed : slowest;

            using var json = JsonDocument.Parse(body);
            AssertDashboard(json.RootElement.GetProperty("page"), kind);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"The slowest post took {slowest}.");
    }

    /// <summary>
    /// Copies the entities, data sources and dashboard page of the PagePerformanceApp fixture into the folder, and adds the
    /// fixture's texts to both locales of the folder, so the page's text keys resolve.
    /// </summary>
    private static void AddDashboard(TemporaryFolder folder)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PagePerformanceApp");
        foreach (var directory in new[] { "entities", "data-sources" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(fixture, directory), "*.json"))
            {
                folder.With($"{directory}/{Path.GetFileName(file)}", File.ReadAllText(file));
            }
        }

        folder.With("pages/dashboard.json", File.ReadAllText(Path.Combine(fixture, "pages", "dashboard.json")));

        var fixtureTexts = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture, "texts", "en.json")))!["texts"]!.AsObject();
        foreach (var locale in new[] { "en", "vi" })
        {
            var relative = $"texts/{locale}.json";
            var file = JsonNode.Parse(File.ReadAllText(Path.Combine(folder.Path, relative)))!;
            var texts = file["texts"]!.AsObject();
            foreach (var (key, value) in fixtureTexts)
            {
                texts[key] = value?.DeepClone();
            }

            folder.With(relative, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>The dashboard draft with the kind of its chart set to <paramref name="kind"/>.</summary>
    private static string Draft(JsonNode dashboard, string kind)
    {
        ChartOf(dashboard)["kind"] = kind;
        return dashboard.ToJsonString();
    }

    private static JsonObject ChartOf(JsonNode dashboard) =>
        dashboard["widgets"]![0]!["tabs"]![0]!["widgets"]![0]!["widgets"]![0]!["columns"]![1]!["widgets"]![0]!.AsObject();

    private static void AssertDashboard(JsonElement page, string kind)
    {
        Assert.NotEqual(JsonValueKind.Null, page.ValueKind);

        var tabs = Assert.Single(page.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("tabs", tabs.GetProperty("type").GetString());
        var tab = Assert.Single(tabs.GetProperty("tabs").EnumerateArray().ToList());
        var section = Assert.Single(tab.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("section", section.GetProperty("type").GetString());
        var inSection = section.GetProperty("widgets").EnumerateArray().ToList();
        Assert.Equal(["columns", "table"], inSection.Select(widget => widget.GetProperty("type").GetString()));
        var columns = inSection[0].GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal([4, 8], columns.Select(column => column.GetProperty("width").GetInt32()));
        Assert.Equal(["kpi", "kpi", "kpi"], columns[0].GetProperty("widgets").EnumerateArray().Select(widget => widget.GetProperty("type").GetString()));
        var chart = Assert.Single(columns[1].GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("chart", chart.GetProperty("type").GetString());
        Assert.Equal(kind, chart.GetProperty("chart").GetProperty("kind").GetString());

        AssertNoPlaceholder(page.GetProperty("widgets"));
    }

    private static void AssertNoPlaceholder(JsonElement widgets)
    {
        foreach (var widget in widgets.EnumerateArray())
        {
            Assert.False(widget.TryGetProperty("placeholder", out _), $"Widget {widget.GetProperty("path").GetString()} is a placeholder.");
            if (widget.TryGetProperty("widgets", out var nested))
            {
                AssertNoPlaceholder(nested);
            }

            if (widget.TryGetProperty("tabs", out var tabs))
            {
                foreach (var tab in tabs.EnumerateArray())
                {
                    AssertNoPlaceholder(tab.GetProperty("widgets"));
                }
            }

            if (widget.TryGetProperty("columns", out var columns))
            {
                foreach (var column in columns.EnumerateArray())
                {
                    AssertNoPlaceholder(column.GetProperty("widgets"));
                }
            }
        }
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
            builder.UseTestAuthentication(new TestIdentity("hoa", "Hoa Author", "author"));
        });

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string draft)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(PageUrl, UriKind.Relative))
        {
            Content = new StringContent(draft, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<(TimeSpan Elapsed, string Body)> TimedPostAsync(HttpClient client, string draft)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await PostAsync(client, draft);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (stopwatch.Elapsed, body);
    }
}
