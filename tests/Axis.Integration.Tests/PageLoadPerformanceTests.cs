using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;
using static Axis.Integration.Tests.PerformanceServerFixture;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A dashboard page loads at a realistic size within the time limit: the page's metadata, then the data of
/// its table, chart and KPIs, all read from 100,000 requests. The page holds tabs, a section and columns,
/// three levels of widget lists, as the SPA loads it. The class runs alone, so no other test competes for CPU
/// with the timed loads.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class PageLoadPerformanceTests(PageLoadPerformanceTests.PageFixture fixture) : IClassFixture<PageLoadPerformanceTests.PageFixture>
{
    private const int RequestCount = 100_000;
    private const int DepartmentCount = 1_000;
    private const int WarmUpLoads = 3;
    private const int Attempts = 3;
    private const string PageUrl = "/api/sites/performance/pages/dashboard";

    // One load is the metadata and the data of five widgets read in parallel: a page of the table with its
    // count and department names, a chart over three groups and three totals. Each of the widget reads is one
    // pass over the 100,000 requests. The load took about 70 ms locally. The limit is well above that, so
    // shared CI runners do not make the test flaky, and it still catches a slowdown of ten times or more. The best
    // of three loads counts, because a runner can pause the process for a garbage collection or another job.
    private static readonly TimeSpan _loadTime = TimeSpan.FromSeconds(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_dashboard_with_a_table_a_chart_and_three_kpis_over_a_hundred_thousand_requests_loads_within_the_time_limit()
    {
        for (var warmUp = 0; warmUp < WarmUpLoads; warmUp++)
        {
            await LoadAsync();
        }

        var best = await LoadAsync();
        for (var attempt = 1; attempt < Attempts; attempt++)
        {
            var next = await LoadAsync();
            best = next.Elapsed < best.Elapsed ? next : best;
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"The best of {Attempts} loads took {best.Elapsed}.");
        Assert.True(best.Elapsed < _loadTime, $"The page took {best.Elapsed} at best of {Attempts} loads.");

        AssertTree(best.Metadata);

        // The data is what the seed inserted, so the loads cannot pass on empty answers.
        var table = best.Rows["RequestList"];
        Assert.Equal(RequestCount, table.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(50, table.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(
            ["approved", "open", "rejected"],
            best.Rows["RequestsByStatus"].RootElement.GetProperty("items").EnumerateArray()
                .Select(group => group.GetProperty("values").GetProperty("status").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(RequestCount, best.Rows["RequestsByStatus"].RootElement.GetProperty("items").EnumerateArray()
            .Sum(group => group.GetProperty("values").GetProperty("requests").GetInt32()));
        Assert.Equal(StatusCount(0), Kpi(best, "OpenRequestTotals", "openCount"));
        Assert.Equal(Enumerable.Range(1, RequestCount).Sum(n => (long)Amount(n)), Kpi(best, "AmountTotals", "amountSum"));
        Assert.Equal(99, Kpi(best, "LargestRequestTotals", "amountMax"));
    }

    /// <summary>The amount of request <paramref name="n"/>, so 1 in 100 requests has amount 0.</summary>
    private static int Amount(int n) => n % 100;

    /// <summary>The requests whose status is the <paramref name="index"/>th of open, approved and rejected.</summary>
    private static long StatusCount(int index) => Enumerable.Range(1, RequestCount).Count(n => n % 3 == index);

    private static long Kpi(Load load, string dataSource, string measure)
    {
        var row = Assert.Single(load.Rows[dataSource].RootElement.GetProperty("items").EnumerateArray().ToList());
        return row.GetProperty("values").GetProperty(measure).GetInt64();
    }

    /// <summary>
    /// Loads the page as the SPA does: the metadata first, then the rows of the data source of each widget in
    /// parallel with the query the widget sends. A table sends none, a chart sends its page size and a KPI none.
    /// </summary>
    private async Task<Load> LoadAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var metadata = await GetAsync(PageUrl);
        var requests = Leaves(metadata.RootElement.GetProperty("widgets"))
            .Where(widget => widget.GetProperty("dataSource").ValueKind == JsonValueKind.Object)
            .Select(widget => (
                Name: widget.GetProperty("dataSource").GetProperty("name").GetString()!,
                Url: widget.GetProperty("dataSource").GetProperty("rowsPath").GetString()! + (widget.GetProperty("type").GetString() == "chart" ? "?pageSize=100" : string.Empty)))
            .ToList();
        var answers = await Task.WhenAll(requests.Select(request => GetAsync(request.Url)));
        stopwatch.Stop();

        return new Load(stopwatch.Elapsed, metadata, requests.Zip(answers).ToDictionary(pair => pair.First.Name, pair => pair.Second, StringComparer.Ordinal));
    }

    private async Task<JsonDocument> GetAsync(string url)
    {
        using var request = Request(url, Host);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>The page holds tabs, then a section, then columns with the chart and the KPIs, and the table next to the columns.</summary>
    private static void AssertTree(JsonDocument metadata)
    {
        var tabs = Assert.Single(metadata.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("tabs", tabs.GetProperty("type").GetString());
        var tab = Assert.Single(tabs.GetProperty("tabs").EnumerateArray().ToList());
        var section = Assert.Single(tab.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("section", section.GetProperty("type").GetString());
        var inSection = section.GetProperty("widgets").EnumerateArray().ToList();
        Assert.Equal(["columns", "table"], inSection.Select(widget => widget.GetProperty("type").GetString()));
        var columns = inSection[0].GetProperty("columns").EnumerateArray().ToList();
        Assert.Equal([4, 8], columns.Select(column => column.GetProperty("width").GetInt32()));
        Assert.Equal(["kpi", "kpi", "kpi"], columns[0].GetProperty("widgets").EnumerateArray().Select(widget => widget.GetProperty("type").GetString()));
        Assert.Equal(["chart"], columns[1].GetProperty("widgets").EnumerateArray().Select(widget => widget.GetProperty("type").GetString()));
    }

    /// <summary>The widgets that show something, not the containers around them, depth first in file order.</summary>
    private static IEnumerable<JsonElement> Leaves(JsonElement widgets)
    {
        foreach (var widget in widgets.EnumerateArray())
        {
            var lists = widget.GetProperty("type").GetString() switch
            {
                "columns" => widget.GetProperty("columns").EnumerateArray().Select(column => column.GetProperty("widgets")),
                "tabs" => widget.GetProperty("tabs").EnumerateArray().Select(tab => tab.GetProperty("widgets")),
                "section" => [widget.GetProperty("widgets")],
                _ => null,
            };
            if (lists is null)
            {
                yield return widget;
                continue;
            }

            foreach (var leaf in lists.SelectMany(Leaves))
            {
                yield return leaf;
            }
        }
    }

    private sealed record Load(TimeSpan Elapsed, JsonDocument Metadata, IReadOnlyDictionary<string, JsonDocument> Rows);

    /// <summary>
    /// The PagePerformanceApp fixture with 1,000 departments and 100,000 requests. Request n belongs to
    /// department n % 1000, has amount n % 100, and is open, approved or rejected as n % 3 is 0, 1 or 2.
    /// </summary>
    public sealed class PageFixture : PerformanceServerFixture
    {
        public PageFixture()
            : base("PagePerformanceApp", "analyst")
        {
        }

        protected override async Task SeedAsync(NpgsqlDataSource dataSource, ApplicationModel model)
        {
            Assert.True(model.TryGetEntity("Department", out var department));
            Assert.True(model.TryGetEntity("Request", out var request));
            var departments = Enumerable.Range(0, DepartmentCount).Select(_ => Guid.NewGuid()).ToArray();
            var id = EntityNaming.Quote(EntityNaming.IdColumn);
            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(department)} ({id}, {Column("name")})
                SELECT (@departments::uuid[])[k + 1], 'Dept ' || k
                FROM generate_series(0, {DepartmentCount - 1}) AS k
                """))
            {
                insert.Parameters.AddWithValue("departments", departments);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            await using (var insert = dataSource.CreateCommand(
                $"""
                INSERT INTO {Table(request)} ({id}, {Column("title")}, {Column("amount")}, {Column("department")}, {Column("status")}, {Column("createdOn")})
                SELECT gen_random_uuid(), 'Request ' || n, n % 100, (@departments::uuid[])[1 + n % {DepartmentCount}],
                    (ARRAY['open', 'approved', 'rejected'])[1 + n % 3], DATE '2026-01-01' + (n % 365)
                FROM generate_series(1, {RequestCount}) AS n
                """))
            {
                insert.Parameters.AddWithValue("departments", departments);
                await insert.ExecuteNonQueryAsync(CancellationToken);
            }

            // Updates the planner's statistics, as autovacuum does in production.
            await using (var analyze = dataSource.CreateCommand($"ANALYZE {Table(department)}, {Table(request)}"))
            {
                await analyze.ExecuteNonQueryAsync(CancellationToken);
            }
        }
    }
}
