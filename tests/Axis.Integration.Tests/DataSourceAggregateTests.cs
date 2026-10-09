using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Grouped data sources return one row per group of the rows that pass the filter, with count,
/// sum, min and max per group. Paging and sorting apply to the groups.
/// </summary>
public sealed class DataSourceAggregateTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    // ItemTotalsByDepartment groups items by department.name as departmentName and by department,
    // with items = count, quantitySum = sum(quantity), priceMin and priceMax over price,
    // firstNeededBy = min(neededBy) and lastOrderedAt = max(orderedAt), sorted by departmentName.
    private const string ByDepartment = "/api/apps/RecordsApp/data-sources/ItemTotalsByDepartment/rows";

    // ItemTotals has no group field and keeps active items, with items = count,
    // quantitySum = sum(quantity), priceSum = sum(price) and priceMax = max(price).
    private const string Totals = "/api/apps/RecordsApp/data-sources/ItemTotals/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_group_holds_its_group_fields_then_count_sum_min_and_max_of_its_items_including_the_null_group()
    {
        var ids = await SeedAsync();

        using var rows = await GetJsonAsync(ByDepartment);

        // NULL sorts last in ascending order. Every value is the raw JSON the endpoint wrote.
        Assert.Equal((1, 20, 4L), Paging(rows));
        var items = rows.RootElement.GetProperty("items");
        Assert.All(items.EnumerateArray(), item =>
        {
            Assert.Equal(["id", "values", "labels"], item.EnumerateObject().Select(property => property.Name));
            Assert.Equal(JsonValueKind.Null, item.GetProperty("id").ValueKind);
        });
        Assert.Equal(
            [
                $$"""{"departmentName":"Finance","department":"{{ids["Finance"]:D}}","items":1,"quantitySum":4,"priceMin":2.00,"priceMax":2.00,"firstNeededBy":null,"lastOrderedAt":null}""",
                $$"""{"departmentName":"Ops","department":"{{ids["Ops"]:D}}","items":1,"quantitySum":4,"priceMin":7.00,"priceMax":7.00,"firstNeededBy":"2026-04-01","lastOrderedAt":"2026-02-01T10:00:00.000000Z"}""",
                $$"""{"departmentName":"Sales","department":"{{ids["Sales"]:D}}","items":3,"quantitySum":7,"priceMin":3.25,"priceMax":10.50,"firstNeededBy":"2026-02-15","lastOrderedAt":"2026-01-05T00:00:00.000000Z"}""",
                """{"departmentName":null,"department":null,"items":2,"quantitySum":null,"priceMin":1.00,"priceMax":1.00,"firstNeededBy":null,"lastOrderedAt":null}""",
            ],
            items.EnumerateArray().Select(item => item.GetProperty("values").GetRawText()));

        // A group field that ends at a reference keeps its label, as in an ungrouped row.
        Assert.Equal(
            ["""{"department":"Finance"}""", """{"department":"Ops"}""", """{"department":"Sales"}""", "{}"],
            items.EnumerateArray().Select(item => item.GetProperty("labels").GetRawText()));
    }

    [Fact]
    public async Task Sort_by_a_measure_and_paging_apply_to_the_groups_with_the_group_fields_as_tie_break()
    {
        await SeedAsync();

        using var descending = await GetJsonAsync($"{ByDepartment}?sort=-quantitySum");
        using var second = await GetJsonAsync($"{ByDepartment}?sort=-quantitySum&pageSize=1&page=2");
        using var byCount = await GetJsonAsync($"{ByDepartment}?sort=items");
        using var byName = await GetJsonAsync($"{ByDepartment}?sort=-departmentName&pageSize=3");
        using var pastTheEnd = await GetJsonAsync($"{ByDepartment}?page=3&pageSize=2");

        // NULL sorts first in descending order. Finance and Ops tie on quantitySum and on items, so
        // departmentName breaks the tie, ascending whatever the sort direction.
        Assert.Equal([null, "Sales", "Finance", "Ops"], Groups(descending));
        Assert.Equal((1, 20, 4L), Paging(descending));
        Assert.Equal(["Sales"], Groups(second));
        Assert.Equal((2, 1, 4L), Paging(second));
        Assert.Equal(["Finance", "Ops", null, "Sales"], Groups(byCount));
        Assert.Equal([null, "Sales", "Ops"], Groups(byName));
        Assert.Equal((1, 3, 4L), Paging(byName));
        Assert.Empty(Groups(pastTheEnd));
        Assert.Equal((3, 2, 4L), Paging(pastTheEnd));
    }

    [Fact]
    public async Task Data_source_with_no_group_field_returns_one_total_row_over_the_filtered_items_even_when_none_pass()
    {
        await fixture.ResetAsync();

        using var empty = await GetJsonAsync(Totals);

        await SeedAsync();
        using var totals = await GetJsonAsync(Totals);

        // With no row, count is 0 and a sum or max over no value is null, not 0.
        Assert.Equal((1, 20, 1L), Paging(empty));
        var emptyRow = Assert.Single(empty.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, emptyRow.GetProperty("id").ValueKind);
        Assert.Equal(
            """{"items":0,"quantitySum":null,"priceSum":null,"priceMax":null}""",
            emptyRow.GetProperty("values").GetRawText());
        Assert.Equal("{}", emptyRow.GetProperty("labels").GetRawText());

        // Only S1, S2 and F1 are active.
        Assert.Equal((1, 20, 1L), Paging(totals));
        Assert.Equal(
            """{"items":3,"quantitySum":11,"priceSum":15.75,"priceMax":10.50}""",
            Assert.Single(totals.RootElement.GetProperty("items").EnumerateArray()).GetProperty("values").GetRawText());
    }

    [Theory]
    [InlineData("sort=quantity")]
    [InlineData("sort=department")]
    [InlineData("sort=Items")]
    [InlineData("sort=-QuantitySum")]
    public async Task Sort_that_names_no_group_field_or_measure_or_a_group_reference_is_a_400_problem(string query)
    {
        await fixture.ResetAsync();
        using var request = Request($"{ByDepartment}?{query}", HostA);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var body = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["sort"], body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    /// <summary>
    /// Empties both tenants, then inserts departments Sales, Ops and Finance and these items in
    /// tenant A: S1 and S2 with values and S3 without in Sales, O1 in Ops, F1 in Finance, and N1
    /// with only a price and N2 with no value in no department. Only S1, S2 and F1 are active.
    /// Returns the department ids by name.
    /// </summary>
    private async Task<Dictionary<string, Guid>> SeedAsync()
    {
        await fixture.ResetAsync();
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in new[] { "Sales", "Ops", "Finance" })
        {
            ids[name] = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = name });
        }

        var items = new (string Name, string? Department, string? Quantity, string? Price, string? NeededBy, string? OrderedAt, string? Active)[]
        {
            ("S1", "Sales", "2", "10.50", "2026-03-01", "2026-01-02T03:04:05Z", "true"),
            ("S2", "Sales", "5", "3.25", "2026-02-15", "2026-01-05T00:00:00Z", "true"),
            ("S3", "Sales", null, null, null, null, null),
            ("O1", "Ops", "4", "7.00", "2026-04-01", "2026-02-01T10:00:00Z", "false"),
            ("F1", "Finance", "4", "2.00", null, null, "true"),
            ("N1", null, null, "1.00", null, null, null),
            ("N2", null, null, null, null, null, null),
        };
        foreach (var item in items)
        {
            await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
            {
                ["name"] = item.Name,
                ["department"] = item.Department is null ? null : ids[item.Department].ToString(),
                ["quantity"] = item.Quantity,
                ["price"] = item.Price,
                ["neededBy"] = item.NeededBy,
                ["orderedAt"] = item.OrderedAt,
                ["active"] = item.Active,
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

    private static (int Page, int PageSize, long TotalCount) Paging(JsonDocument rows) =>
        (rows.RootElement.GetProperty("page").GetInt32(),
            rows.RootElement.GetProperty("pageSize").GetInt32(),
            rows.RootElement.GetProperty("totalCount").GetInt64());

    /// <summary>The departmentName of each group, in response order.</summary>
    private static List<string?> Groups(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("values").GetProperty("departmentName").GetString())
            .ToList();
}
