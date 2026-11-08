using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>filter</c> of a record list keeps the records that match conditions in nested All and Any groups, on top of the
/// user's read filters, and treats every value as plain data. It runs on the Item entity of the RecordsApp fixture.
/// <c>fia</c> may read only the name and the department of items.
/// </summary>
public sealed class RecordListFilterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    // Stands for no record, as an empty text stands for the one whose name is empty.
    private const string None = "-";

    private const string Everything = "Alpha|O'Brien|100% wool|100 wool|";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":"Alpha"}]}""", "Alpha")]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":"alpha"}]}""", None)]
    [InlineData("""{"all":[{"field":"name","op":"ne","value":"Alpha"}]}""", "O'Brien|100% wool|100 wool|")]
    [InlineData("""{"all":[{"field":"name","op":"contains","value":"WOOL"}]}""", "100% wool|100 wool")]
    [InlineData("""{"all":[{"field":"name","op":"empty"}]}""", "")]
    [InlineData("""{"all":[{"field":"name","op":"notEmpty"}]}""", "Alpha|O'Brien|100% wool|100 wool")]
    [InlineData("""{"all":[{"field":"status","op":"eq","value":"open"}]}""", "Alpha|100% wool")]
    [InlineData("""{"all":[{"field":"status","op":"ne","value":"open"}]}""", "O'Brien|100 wool|")]
    [InlineData("""{"all":[{"field":"status","op":"empty"}]}""", "100 wool|")]
    [InlineData("""{"all":[{"field":"status","op":"notEmpty"}]}""", "Alpha|O'Brien|100% wool")]
    [InlineData("""{"all":[{"field":"quantity","op":"eq","value":3}]}""", "Alpha")]
    [InlineData("""{"all":[{"field":"quantity","op":"ne","value":3}]}""", "O'Brien|100% wool|100 wool|")]
    [InlineData("""{"all":[{"field":"quantity","op":"gt","value":3}]}""", "O'Brien|100% wool")]
    [InlineData("""{"all":[{"field":"quantity","op":"gte","value":5}]}""", "O'Brien|100% wool")]
    [InlineData("""{"all":[{"field":"quantity","op":"lt","value":3}]}""", "")]
    [InlineData("""{"all":[{"field":"quantity","op":"lte","value":3}]}""", "Alpha|")]
    [InlineData("""{"all":[{"field":"quantity","op":"empty"}]}""", "100 wool")]
    [InlineData("""{"all":[{"field":"price","op":"gte","value":10.5}]}""", "Alpha|O'Brien")]
    [InlineData("""{"all":[{"field":"price","op":"lt","value":10.5}]}""", "100% wool")]
    [InlineData("""{"all":[{"field":"price","op":"eq","value":20}]}""", "O'Brien")]
    [InlineData("""{"all":[{"field":"neededBy","op":"eq","value":"2026-03-01"}]}""", "Alpha")]
    [InlineData("""{"all":[{"field":"neededBy","op":"gt","value":"2026-03-01"}]}""", "O'Brien|100% wool")]
    [InlineData("""{"all":[{"field":"neededBy","op":"lte","value":"2026-06-01"}]}""", "Alpha|O'Brien")]
    [InlineData("""{"all":[{"field":"neededBy","op":"empty"}]}""", "100 wool|")]
    [InlineData("""{"all":[{"field":"orderedAt","op":"lt","value":"2026-04-01T00:00:00Z"}]}""", "Alpha")]
    [InlineData("""{"all":[{"field":"orderedAt","op":"gte","value":"2026-06-01T10:00:00Z"}]}""", "O'Brien")]
    [InlineData("""{"all":[{"field":"orderedAt","op":"notEmpty"}]}""", "Alpha|O'Brien")]
    public async Task A_condition_keeps_exactly_the_records_that_match_and_counts_them(string filter, string expected)
    {
        await SeedAsync();

        var (names, total) = await ListAsync(fixture.Client, filter);

        Assert.Equal(Names(expected), names);
        Assert.Equal(names.Count, total);
    }

    [Fact]
    public async Task Nested_All_and_Any_groups_combine_their_conditions_and_the_count_follows()
    {
        await SeedAsync();
        const string filter = """
            {"all":[
              {"field":"status","op":"eq","value":"open"},
              {"any":[
                {"field":"quantity","op":"gt","value":5},
                {"all":[{"field":"name","op":"contains","value":"alp"},{"field":"neededBy","op":"lt","value":"2026-04-01"}]},
                {"any":[]}
              ]}
            ]}
            """;

        var (names, total) = await ListAsync(fixture.Client, filter);
        var (anyNames, anyTotal) = await ListAsync(fixture.Client, """{"any":[{"field":"name","op":"eq","value":"Alpha"},{"all":[{"field":"status","op":"empty"},{"field":"quantity","op":"empty"}]}]}""");

        Assert.Equal(Names("Alpha|100% wool"), names);
        Assert.Equal(2, total);
        Assert.Equal(Names("Alpha|100 wool"), anyNames);
        Assert.Equal(2, anyTotal);
    }

    [Fact]
    public async Task A_filter_narrows_a_page_and_the_count_follows_the_filter_not_the_page()
    {
        await SeedAsync();

        using var page = await GetJsonAsync(fixture.Client, $"{Items}?pageSize=1&filter={Encode("""{"all":[{"field":"name","op":"contains","value":"wool"}]}""")}");

        Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(2, page.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"all":[]}""")]
    [InlineData("""{"any":[{"all":[]}]}""")]
    public async Task An_empty_filter_means_no_filter(string filter)
    {
        await SeedAsync();

        var (names, total) = await ListAsync(fixture.Client, filter);

        Assert.Equal(Names(Everything), names);
        Assert.Equal(5, total);
    }

    [Fact]
    public async Task A_filter_on_a_field_the_user_may_not_read_is_a_400_at_filter_with_the_message_of_an_unknown_field()
    {
        await SeedAsync();
        const string OnPrice = """{"all":[{"field":"price","op":"gt","value":1}]}""";
        const string OnName = """{"all":[{"field":"name","op":"contains","value":"o"}]}""";
        using var fia = fixture.CreateClient().SignedInAs("fia");

        var unknown = await ErrorAsync(fia, """{"all":[{"field":"nonsense","op":"gt","value":1}]}""");
        var hidden = await ErrorAsync(fia, OnPrice);

        Assert.Equal(unknown, hidden);
        var (_, total) = await ListAsync(fixture.Client, OnPrice);
        Assert.Equal(3, total);
        var (_, everyone) = await ListAsync(fixture.Client, OnName);
        var (_, readable) = await ListAsync(fia, OnName);
        Assert.Equal(3, everyone);
        Assert.Equal(everyone, readable);
    }

    [Theory]
    [InlineData("""{"all":[{"field":"active","op":"eq","value":true}]}""")]
    [InlineData("""{"all":[{"field":"department","op":"empty"}]}""")]
    [InlineData("""{"all":[{"field":"parts","op":"empty"}]}""")]
    [InlineData("""{"all":[{"field":"quantity","op":"contains","value":"1"}]}""")]
    [InlineData("""{"all":[{"field":"status","op":"eq","value":"archived"}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"eq","value":null}]}""")]
    [InlineData("""{"all":[{"field":"name","op":"eq"}]}""")]
    [InlineData("""{"all":[""")]
    public async Task A_filter_that_names_no_usable_field_or_is_malformed_is_a_400_at_filter(string filter)
    {
        using var response = await fixture.Client.SendAsync(Request(HttpMethod.Get, $"{Items}?filter={Encode(filter)}", HostA), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var message = Assert.Single(problem.RootElement.GetProperty("errors").GetProperty("filter").EnumerateArray()).GetString();
        Assert.DoesNotContain("archived", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repeated_filter_is_a_400_at_filter()
    {
        var filter = Encode("""{"all":[{"field":"name","op":"empty"}]}""");

        using var response = await fixture.Client.SendAsync(Request(HttpMethod.Get, $"{Items}?filter={filter}&filter={filter}", HostA), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("filter", out _));
    }

    [Theory]
    [InlineData("'", "O'Brien")]
    [InlineData("%", "100% wool")]
    [InlineData("0 w", "100 wool")]
    [InlineData("_", None)]
    [InlineData("\\", None)]
    public async Task Contains_matches_a_quote_a_percent_sign_and_the_other_characters_of_LIKE_as_plain_text(string text, string expected)
    {
        await SeedAsync();

        var (names, _) = await ListAsync(fixture.Client, $$"""{"all":[{"field":"name","op":"contains","value":{{JsonSerializer.Serialize(text)}}}]}""");

        Assert.Equal(Names(expected), names);
    }

    [Fact]
    public async Task An_equals_value_that_holds_SQL_matches_nothing_and_leaves_the_table_intact()
    {
        await SeedAsync();
        var injection = JsonSerializer.Serialize("x' OR '1'='1");

        var (names, total) = await ListAsync(fixture.Client, $$"""{"all":[{"field":"name","op":"eq","value":{{injection}}}]}""");
        var (dropped, _) = await ListAsync(fixture.Client, $$"""{"all":[{"field":"name","op":"eq","value":{{JsonSerializer.Serialize("'; DROP TABLE x; --")}}}]}""");
        var (afterwards, afterTotal) = await ListAsync(fixture.Client, "");

        Assert.Empty(names);
        Assert.Equal(0, total);
        Assert.Empty(dropped);
        Assert.Equal(Names(Everything), afterwards);
        Assert.Equal(5, afterTotal);
    }

    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        (string Name, string? Quantity, string? Price, string? NeededBy, string? OrderedAt, string? Status)[] items =
        [
            ("Alpha", "3", "10.50", "2026-03-01", "2026-03-01T10:00:00Z", "open"),
            ("O'Brien", "5", "20.00", "2026-06-01", "2026-06-01T10:00:00Z", "closed"),
            ("100% wool", "10", "5.25", "2026-09-01", null, "open"),
            ("100 wool", null, null, null, null, null),
            ("", "1", null, null, null, null),
        ];
        foreach (var (name, quantity, price, neededBy, orderedAt, status) in items)
        {
            await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
            {
                ["name"] = name,
                ["quantity"] = quantity,
                ["price"] = price,
                ["neededBy"] = neededBy,
                ["orderedAt"] = orderedAt,
                ["status"] = status,
            });
        }
    }

    // The names a list holds, in a fixed order, and its totalCount.
    private static async Task<(List<string> Names, long Total)> ListAsync(HttpClient client, string filter)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Get, $"{Items}?pageSize=100&filter={Encode(filter)}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var names = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("values").GetProperty("name").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
        return (names, body.RootElement.GetProperty("totalCount").GetInt64());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Get, path, HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    // The message at filter of a 400.
    private static async Task<string> ErrorAsync(HttpClient client, string filter)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Get, $"{Items}?filter={Encode(filter)}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal(["filter"], errors.EnumerateObject().Select(property => property.Name));
        return Assert.Single(errors.GetProperty("filter").EnumerateArray()).GetString()!;
    }

    private static List<string> Names(string joined) =>
        [.. (joined == None ? [] : joined.Split('|')).Order(StringComparer.Ordinal)];

    private static string Encode(string filter) => Uri.EscapeDataString(filter);
}
