using Axis.Server.Http;
using Microsoft.AspNetCore.Http;

namespace Axis.Server.Tests;

/// <summary>
/// The list query reads <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> against the columns
/// a list declares and applies them to rows in memory, with the operators of the record API, see
/// <c>docs/reference/record-api.md#paging-and-sorting</c>.
/// </summary>
public sealed class ListQueryTests
{
    private sealed record Row(string Id, string? Name, string? Status, int? Count, decimal? Amount, DateOnly? Day, DateTimeOffset? At);

    private static readonly IReadOnlyList<ListColumn<Row>> _columns =
    [
        new("name", ListColumnKind.Text, row => row.Name, Searchable: true),
        new("status", ListColumnKind.Enum, row => row.Status, Values: ["open", "closed"]),
        new("count", ListColumnKind.Number, row => row.Count),
        new("amount", ListColumnKind.Number, row => row.Amount),
        new("day", ListColumnKind.Date, row => row.Day, Order: row => row.At),
    ];

    private static readonly IReadOnlyList<Row> _rows =
    [
        new("a", "Alpha", "open", 1, 1.5m, new DateOnly(2026, 1, 10), new DateTimeOffset(2026, 1, 10, 9, 0, 0, TimeSpan.Zero)),
        new("b", "beta", "closed", 5, 10m, new DateOnly(2026, 1, 20), new DateTimeOffset(2026, 1, 20, 9, 0, 0, TimeSpan.Zero)),
        new("c", "", null, null, null, null, null),
        new("d", null, "open", 5, 2.25m, new DateOnly(2026, 1, 20), new DateTimeOffset(2026, 1, 20, 18, 0, 0, TimeSpan.Zero)),
        new("e", "Alphabet", "closed", 0, -3m, new DateOnly(2025, 12, 31), new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero)),
    ];

    [Theory]
    [InlineData("name", "eq", "\"Alpha\"", "a")]
    [InlineData("name", "eq", "\"alpha\"", "")]
    [InlineData("name", "ne", "\"Alpha\"", "bcde")]
    [InlineData("name", "contains", "\"ALPH\"", "ae")]
    [InlineData("name", "empty", null, "cd")]
    [InlineData("name", "notEmpty", null, "abe")]
    [InlineData("status", "eq", "\"open\"", "ad")]
    [InlineData("status", "ne", "\"open\"", "bce")]
    [InlineData("status", "empty", null, "c")]
    [InlineData("status", "notEmpty", null, "abde")]
    [InlineData("count", "eq", "5", "bd")]
    [InlineData("count", "ne", "5", "ace")]
    [InlineData("count", "lt", "5", "ae")]
    [InlineData("count", "lte", "5", "abde")]
    [InlineData("count", "gt", "1", "bd")]
    [InlineData("count", "gte", "1", "abd")]
    [InlineData("count", "empty", null, "c")]
    [InlineData("count", "notEmpty", null, "abde")]
    [InlineData("amount", "eq", "10", "b")]
    [InlineData("amount", "eq", "1.50", "a")]
    [InlineData("amount", "ne", "10", "acde")]
    [InlineData("amount", "lt", "2.25", "ae")]
    [InlineData("amount", "gte", "2.25", "bd")]
    [InlineData("amount", "empty", null, "c")]
    [InlineData("day", "eq", "\"2026-01-20\"", "bd")]
    [InlineData("day", "ne", "\"2026-01-20\"", "ace")]
    [InlineData("day", "lt", "\"2026-01-20\"", "ae")]
    [InlineData("day", "lte", "\"2026-01-20\"", "abde")]
    [InlineData("day", "gt", "\"2026-01-10\"", "bd")]
    [InlineData("day", "gte", "\"2026-01-10\"", "abd")]
    [InlineData("day", "empty", null, "c")]
    [InlineData("day", "notEmpty", null, "abde")]
    public void Each_operator_keeps_the_rows_the_record_API_would_match(string field, string op, string? value, string expected)
    {
        var condition = value is null ? $$"""{"field":"{{field}}","op":"{{op}}"}""" : $$"""{"field":"{{field}}","op":"{{op}}","value":{{value}}}""";

        var page = Apply("filter=" + Uri.EscapeDataString($$"""{"all":[{{condition}}]}"""));

        Assert.Equal(expected, Ids(page));
        Assert.Equal(expected.Length, page.TotalCount);
    }

    [Fact]
    public void Nested_groups_are_all_or_any_of_their_children()
    {
        var filter = """{"all":[{"field":"count","op":"gte","value":1},{"any":[{"field":"status","op":"eq","value":"closed"},{"field":"name","op":"eq","value":"Alpha"}]}]}""";

        Assert.Equal("ab", Ids(Apply($"filter={Uri.EscapeDataString(filter)}")));
    }

    [Fact]
    public void A_filter_without_a_condition_means_no_filter()
    {
        Assert.Equal("abcde", Ids(Apply("filter=" + Uri.EscapeDataString("""{"all":[]}"""))));
    }

    [Fact]
    public void A_search_keeps_the_rows_where_a_searchable_column_contains_the_text_in_any_case()
    {
        Assert.Equal("ae", Ids(Apply("search=ALPHA")));
        Assert.Equal("", Ids(Apply("search=open")));
    }

    [Fact]
    public void A_search_and_a_filter_both_apply()
    {
        Assert.Equal("e", Ids(Apply("search=alpha&filter=" + Uri.EscapeDataString("""{"all":[{"field":"status","op":"eq","value":"closed"}]}"""))));
    }

    [Fact]
    public void A_sort_puts_a_missing_value_last_ascending_and_first_descending()
    {
        Assert.Equal("caebd", Ids(Apply("sort=name")));
        Assert.Equal("dbeac", Ids(Apply("sort=-name")));
    }

    [Fact]
    public void A_sort_on_a_number_orders_by_value_and_keeps_the_list_order_among_equal_ones()
    {
        Assert.Equal("eabdc", Ids(Apply("sort=count")));
        Assert.Equal("cbdae", Ids(Apply("sort=-count")));
    }

    [Fact]
    public void A_sort_on_a_date_column_uses_its_order_value()
    {
        Assert.Equal("eabdc", Ids(Apply("sort=day")));
        Assert.Equal("cdbae", Ids(Apply("sort=-day")));
    }

    [Fact]
    public void A_page_holds_its_rows_and_the_total_counts_every_match()
    {
        var page = Apply("page=2&pageSize=2");

        Assert.Equal("cd", Ids(page));
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public void A_page_past_the_end_holds_no_rows_and_the_real_total()
    {
        var page = Apply("page=9&pageSize=2");

        Assert.Empty(page.Items);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public void The_defaults_are_the_first_page_of_twenty()
    {
        var page = Apply("");

        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal("abcde", Ids(page));
    }

    [Theory]
    [InlineData("sort=missing", "sort")]
    [InlineData("sort=-missing", "sort")]
    [InlineData("sort=name&sort=count", "sort")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("search=a&search=b", "search")]
    [InlineData("filter=%7B%7D", "filter")]
    [InlineData("filter=notjson", "filter")]
    [InlineData("filter=%7B%22all%22%3A%5B%7B%22field%22%3A%22missing%22%2C%22op%22%3A%22empty%22%7D%5D%7D", "filter")]
    [InlineData("filter=%7B%22all%22%3A%5B%7B%22field%22%3A%22count%22%2C%22op%22%3A%22contains%22%2C%22value%22%3A%22x%22%7D%5D%7D", "filter")]
    [InlineData("filter=%7B%22all%22%3A%5B%7B%22field%22%3A%22status%22%2C%22op%22%3A%22eq%22%2C%22value%22%3A%22other%22%7D%5D%7D", "filter")]
    [InlineData("filter=a&filter=b", "filter")]
    public void An_invalid_parameter_is_an_error_that_names_it(string query, string name)
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        Read(query, errors);

        Assert.Equal([name], errors.Keys);
    }

    [Fact]
    public void A_search_on_a_list_with_no_searchable_column_is_an_error()
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        ListColumn<Row>[] columns = [new("count", ListColumnKind.Number, row => row.Count)];

        ListQuery<Row>.Read(Request("search=a"), columns, errors);

        Assert.Equal(["search"], errors.Keys);
    }

    private static string Ids(ListPage<Row> page) => string.Concat(page.Items.Select(row => row.Id));

    private static ListPage<Row> Apply(string query) => Read(query).Apply(_rows);

    private static ListQuery<Row> Read(string query, SortedDictionary<string, string[]>? errors = null)
    {
        return ListQuery<Row>.Read(Request(query), _columns, errors ?? new SortedDictionary<string, string[]>(StringComparer.Ordinal));
    }

    private static HttpRequest Request(string query)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query.Length == 0 ? "" : "?" + query);
        return context.Request;
    }
}
