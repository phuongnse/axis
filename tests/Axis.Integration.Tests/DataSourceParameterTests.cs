using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class DataSourceParameterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    // ItemsMatching keeps the items whose name equals the required nameFilter, and each optional
    // parameter of another type narrows the rows only when it is given.
    private const string Rows = "/api/apps/RecordsApp/data-sources/ItemsMatching/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Text_with_a_quote_semicolon_comment_or_percent_matches_only_the_rows_that_equal_it()
    {
        await fixture.ResetAsync();
        var ids = new Dictionary<string, Guid>();
        foreach (var name in new[] { "O'Brien", "OBrien", "a;b", "x--y", "100%", "1000%" })
        {
            ids[name] = await InsertItemAsync(new() { ["name"] = name, ["quantity"] = "1" });
        }

        foreach (var (name, id) in ids)
        {
            using var rows = await GetJsonAsync($"{Rows}?nameFilter={Uri.EscapeDataString(name)}");

            Assert.Equal([id], RowIds(rows));
            Assert.Equal(1, rows.RootElement.GetProperty("totalCount").GetInt64());
        }

        // '%' is not a wildcard, and empty optional parameters are not given.
        using var percent = await GetJsonAsync($"{Rows}?nameFilter=%25");
        using var emptyOptional = await GetJsonAsync(
            $"{Rows}?nameFilter=O%27Brien&minQuantity=&maxPrice=&activeFilter=&neededFrom=&orderedAfter=&statusFilter=&departmentFilter=");

        Assert.Empty(RowIds(percent));
        Assert.Equal([ids["O'Brien"]], RowIds(emptyOptional));
        Assert.Equal(1, emptyOptional.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Each_typed_parameter_narrows_the_rows_only_when_it_is_given()
    {
        await fixture.ResetAsync();
        var departmentId = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });
        var first = await InsertItemAsync(new()
        {
            ["name"] = "Twin",
            ["quantity"] = "1",
            ["price"] = "10.00",
            ["active"] = "false",
            ["neededBy"] = "2026-10-01",
            ["orderedAt"] = "2026-10-01T00:00:00Z",
            ["status"] = "closed",
        });
        var second = await InsertItemAsync(new()
        {
            ["name"] = "Twin",
            ["quantity"] = "5",
            ["price"] = "50.00",
            ["active"] = "true",
            ["neededBy"] = "2026-10-20",
            ["orderedAt"] = "2026-10-08T12:00:00Z",
            ["status"] = "open",
            ["department"] = departmentId.ToString(),
        });
        await InsertItemAsync(new() { ["name"] = "Other", ["quantity"] = "5", ["status"] = "open" });

        var expected = new Dictionary<string, Guid[]>
        {
            [""] = [first, second],
            ["&minQuantity=3"] = [second],
            ["&maxPrice=20.5"] = [first],
            ["&activeFilter=true"] = [second],
            ["&activeFilter=false"] = [first],
            ["&neededFrom=2026-10-10"] = [second],
            // 18:00 at +07:00 is 11:00 UTC, before the second item's 12:00 UTC.
            ["&orderedAfter=2026-10-08T18:00:00%2B07:00"] = [second],
            ["&orderedAfter=2026-10-08T12:00:00Z"] = [],
            ["&statusFilter=open"] = [second],
            [$"&departmentFilter={departmentId:D}"] = [second],
            ["&minQuantity=3&maxPrice=20"] = [],
        };
        foreach (var (query, ids) in expected)
        {
            using var rows = await GetJsonAsync($"{Rows}?nameFilter=Twin{query}&pageSize=10");

            Assert.Equal(ids.OrderBy(id => id.ToString("D"), StringComparer.Ordinal), RowIds(rows));
            Assert.Equal(ids.Length, rows.RootElement.GetProperty("totalCount").GetInt64());
        }
    }

    [Fact]
    public async Task Every_missing_repeated_or_malformed_parameter_is_reported_in_one_400_problem_keyed_by_its_declared_name()
    {
        await fixture.ResetAsync();

        using var malformed = await GetProblemAsync(
            $"{Rows}?minQuantity=abc&maxPrice=1e3&activeFilter=yes&neededFrom=2026-13-01&orderedAfter=2026-10-07&statusFilter=closed&departmentFilter=nope&page=0");

        var errors = malformed.RootElement.GetProperty("errors");
        Assert.Equal(
            ["activeFilter", "departmentFilter", "maxPrice", "minQuantity", "nameFilter", "neededFrom", "orderedAfter", "page", "statusFilter"],
            errors.EnumerateObject().Select(property => property.Name));
        var messages = errors.EnumerateObject().SelectMany(property => property.Value.EnumerateArray()).Select(message => message.GetString()!).ToList();
        foreach (var requestText in new[] { "abc", "1e3", "yes", "2026-13-01", "2026-10-07", "closed", "nope" })
        {
            Assert.DoesNotContain(messages, message => message.Contains(requestText, StringComparison.Ordinal));
        }

        // A repeated parameter is invalid, and a declared name in another letter case is an unknown parameter.
        using var repeated = await GetProblemAsync($"{Rows}?nameFilter=a&nameFilter=b");
        using var otherCase = await GetProblemAsync($"{Rows}?NAMEFILTER=a");
        using var repeatedEmpty = await GetProblemAsync($"{Rows}?nameFilter=a&minQuantity=&minQuantity=");

        Assert.Equal(["nameFilter"], repeated.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(["nameFilter"], otherCase.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(["minQuantity"], repeatedEmpty.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    private Task<Guid> InsertItemAsync(Dictionary<string, string?> values) => fixture.InsertAsync(TenantA, "Item", values);

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<JsonDocument> GetProblemAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        return await ReadProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private static List<Guid> RowIds(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
}
