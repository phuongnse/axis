using System.Net;
using System.Text.Json;
using Axis.Data.DataSources;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Data sources read fields of related records through reference fields with left joins in the
/// page statement. A null reference gives null values and keeps the row.
/// </summary>
public sealed class DataSourceRelationTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    // ItemsByDepartment projects name, department.name as departmentName and department, keeps items
    // whose department is not Archive and matches the optional nameFilter, sorted by departmentName.
    private const string Rows = "/api/apps/RecordsApp/data-sources/ItemsByDepartment/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Projected_path_returns_the_related_value_and_null_with_no_label_when_the_reference_is_not_set()
    {
        var ids = await SeedAsync();

        using var rows = await GetJsonAsync(Rows);

        // NULL sorts last in ascending order, and C is filtered out by its department's name.
        Assert.Equal([ids["D"], ids["A"], ids["B"]], RowIds(rows));
        Assert.Equal(3, rows.RootElement.GetProperty("totalCount").GetInt64());
        var items = rows.RootElement.GetProperty("items");
        Assert.Equal(
            ["\"Ops\"", "\"Sales\"", "null"],
            items.EnumerateArray().Select(item => item.GetProperty("values").GetProperty("departmentName").GetRawText()));
        Assert.Equal(
            [("name", "\"D\""), ("departmentName", "\"Ops\""), ("department", $"\"{ids["Ops"]:D}\"")],
            items[0].GetProperty("values").EnumerateObject().Select(property => (property.Name, property.Value.GetRawText())));
        Assert.Equal("""{"department":"Ops"}""", items[0].GetProperty("labels").GetRawText());
        Assert.Equal("""{"department":"Sales"}""", items[1].GetProperty("labels").GetRawText());
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("values").GetProperty("department").ValueKind);
        Assert.Equal("{}", items[2].GetProperty("labels").GetRawText());
    }

    [Fact]
    public async Task Sort_on_a_related_field_and_filter_on_a_path_with_a_parameter_return_the_expected_rows_and_count()
    {
        var ids = await SeedAsync();

        using var descending = await GetJsonAsync($"{Rows}?sort=-departmentName");
        using var paged = await GetJsonAsync($"{Rows}?pageSize=1&page=2");
        using var named = await GetJsonAsync($"{Rows}?nameFilter=A");
        using var archived = await GetJsonAsync($"{Rows}?nameFilter=C");

        // NULL sorts first in descending order.
        Assert.Equal([ids["B"], ids["A"], ids["D"]], RowIds(descending));
        Assert.Equal(3, descending.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal([ids["A"]], RowIds(paged));
        Assert.Equal(3, paged.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal([ids["A"]], RowIds(named));
        Assert.Equal(1, named.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Empty(RowIds(archived));
        Assert.Equal(0, archived.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task A_page_runs_as_one_statement_plus_the_count_whatever_its_size(int pageSize)
    {
        await fixture.ResetAsync();
        var departments = new List<Guid>();
        foreach (var name in new[] { "Sales", "Ops", "Finance" })
        {
            departments.Add(await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = name }));
        }

        for (var index = 0; index < 30; index++)
        {
            await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
            {
                ["name"] = $"Item {index:D2}",
                ["department"] = departments[index % departments.Count].ToString(),
            });
        }

        Assert.True(fixture.Model.TryGetDataSource("ItemsByDepartment", out var dataSource));
        var log = new CapturingLoggerFactory();
        await using var npgsql = new NpgsqlDataSourceBuilder(fixture.ConnectionString(TenantA)).UseLoggerFactory(log).Build();
        await using var connection = await npgsql.OpenConnectionAsync(CancellationToken);
        log.Clear();

        var page = await DataSourceQueries.ListAsync(
            connection, fixture.Model, dataSource, 1, pageSize, DataSourceSort.Default(dataSource), [new(dataSource.Parameters[0], null)], CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(Math.Min(pageSize, 30), page.Items.Count);
        Assert.Equal(30, page.TotalCount);
        Assert.All(page.Items, row => Assert.NotNull(row.Values["departmentName"]));
        // Npgsql logs one entry per executed command. Matching on the message alone keeps the test
        // independent of the log category.
        var commands = log.Entries.Count(entry => entry.Message.StartsWith("Command execution completed", StringComparison.Ordinal));
        Assert.True(
            commands == 2,
            $"Expected 2 commands, found {commands}:\n{string.Join('\n', log.Entries.Select(entry => $"{entry.Category}: {entry.Message}"))}");
    }

    /// <summary>
    /// Empties both tenants, then inserts departments Sales, Ops and Archive and items A in Sales,
    /// B with no department, C in Archive and D in Ops in tenant A. Returns the ids by name.
    /// </summary>
    private async Task<Dictionary<string, Guid>> SeedAsync()
    {
        await fixture.ResetAsync();
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in new[] { "Sales", "Ops", "Archive" })
        {
            ids[name] = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = name });
        }

        foreach (var (name, department) in new[] { ("A", "Sales"), ("B", null), ("C", "Archive"), ("D", "Ops") })
        {
            ids[name] = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
            {
                ["name"] = name,
                ["department"] = department is null ? null : ids[department].ToString(),
            });
        }

        return ids;
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> RowIds(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    /// <summary>Keeps every entry logged at any level, with its category, in the order logged.</summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<(string Category, string Message)> _entries = [];

        public IReadOnlyList<(string Category, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerFactory factory, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (factory._entries)
                {
                    factory._entries.Add((category, formatter(state, exception)));
                }
            }
        }
    }
}
