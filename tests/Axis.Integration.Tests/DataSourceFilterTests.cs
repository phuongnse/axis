using System.Globalization;
using System.Net;
using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Data source filters run as SQL against real PostgreSQL. The SQL must keep exactly the rows the
/// interpreter evaluates to <c>true</c>, so both back ends agree on every filter in the subset.
/// </summary>
public sealed class DataSourceFilterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Nulls are spread over every optional field, so each filter meets them.
    private static readonly SeedItem[] _seed =
    [
        new("A", 1, 12.50m, true, new DateOnly(2026, 10, 1), Utc(2026, 10, 7, 10), "open", HasDepartment: true),
        new("Ba", 3, 7.25m, false, new DateOnly(2026, 10, 15), Utc(2026, 10, 8, 9), "closed", HasDepartment: false),
        new("Cx", null, null, true, null, null, null, HasDepartment: true),
        new("B", 5, 10.00m, null, new DateOnly(2026, 9, 30), Utc(2026, 10, 9, 0), "open", HasDepartment: false),
        new("Dax", 12, 7.00m, true, new DateOnly(2025, 12, 31), null, "closed", HasDepartment: true),
        new("Ex", 2, null, false, null, Utc(2026, 10, 1, 0), null, HasDepartment: false),
        new("Fa", 4, 4.00m, true, new DateOnly(2026, 11, 20), Utc(2026, 10, 8, 5), "open", HasDepartment: true),
    ];

    public static TheoryData<string> Filters =>
    [
        "quantity > 2",
        "quantity is null or quantity * 2 + 1 >= 5",
        "price <= 10.5 and active == true",
        "status in ('open')",
        "not (status in ('closed'))",
        "name != 'B'",
        "neededBy < date('2026-10-10')",
        "orderedAt >= dateTime('2026-10-08T00:00:00Z')",
        "orderedAt < dateTime('2026-10-08T12:00:00+07:00')",
        "department is not null",
        "contains(name, 'a') or startsWith(name, 'B')",
        "endsWith(coalesce(name, 'x'), 'x')",
        "if(active, quantity, 0) > 1",
        "length(concat(name, 'zz')) > 3",
        "year(neededBy) == 2026 and daysBetween(neededBy, addDays(neededBy, 3)) == 3",
        "month(neededBy) == 10 and day(neededBy) > 9",
        "round(price, 1) == 12.5 or floor(price) == 7",
        "ceiling(price) == 8",
        "abs(quantity - 10) < 5",
        "active or quantity == 1",
        "-quantity < -1",
        "price == quantity",
        "price + 1.5 > quantity * 2",
        "null == status",
    ];

    // The interpreter cannot read related records, so a path argument is compared with a flattened
    // name that the test supplies.
    public static TheoryData<string, string> RuleFilters => new()
    {
        { "IsBig(quantity)", "IsBig(quantity)" },
        { "not IsBig(quantity)", "not IsBig(quantity)" },
        { "AtLeast(quantity, 7.25)", "AtLeast(quantity, 7.25)" },
        { "AtLeast(price, quantity)", "AtLeast(price, quantity)" },
        { "BothBig(quantity, length(name))", "BothBig(quantity, length(name))" },
        { "InSales(department.name)", "InSales(departmentName)" },
        { "MinOk(quantity, minQuantity)", "MinOk(quantity, minQuantity)" },
        { "Twice(quantity) > 5", "Twice(quantity) > 5" },
        { "Mentions(name, 'a')", "Mentions(name, 'a')" },
    };

    private static readonly DataSourceParameterModel _minQuantity = new("minQuantity", FieldType.Integer, false, null, null, null);

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Filter_keeps_exactly_the_rows_the_interpreter_evaluates_to_true(string filter)
    {
        var rows = await SeedAsync();
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        Assert.True(item.TryGetField("name", out var nameField));
        var compiled = ExpressionModel.Compile(filter, ExpressionScopes.ForEntity(item.Fields), ExpressionType.Boolean);
        var dataSource = new DataSourceModel
        {
            Id = Guid.NewGuid(),
            Name = "Filtered",
            File = "data-sources/filtered.json",
            Entity = new EntityReference(item.Id, item.Name),
            Fields = [new DataSourceFieldModel("name", nameField)],
            Filter = compiled,
        };

        var expected = new List<Guid>();
        foreach (var (id, values) in rows)
        {
            var evaluated = ExpressionInterpreter.Evaluate(compiled.Syntax, compiled.Check, new ExpressionValues(values));
            Assert.True(evaluated.Succeeded, evaluated.Error?.Message);
            if (evaluated.Value is true)
            {
                expected.Add(id);
            }
        }

        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var page = await DataSourceQueries.ListAsync(connection, fixture.Model, dataSource, 1, 100, null, [], CancellationToken);

        // Every filter keeps some rows and drops some, so it tells the two back ends apart.
        Assert.InRange(expected.Count, 1, rows.Count - 1);
        Assert.NotNull(page);
        Assert.Equal(Sorted(expected), Sorted(page.Items.Select(row => row.Id!.Value)));
        Assert.Equal(expected.Count, page.TotalCount);
    }

    [Theory]
    [MemberData(nameof(RuleFilters))]
    public async Task Filter_that_calls_rules_keeps_exactly_the_rows_the_interpreter_evaluates_to_true(string filter, string interpreterFilter)
    {
        var rows = await SeedAsync();

        var (sqlIds, interpreterIds) = await RuleFilterRowsAsync(rows, filter, interpreterFilter, minQuantity: 3L);

        // Every filter keeps some rows and drops some, so it tells the two back ends apart.
        Assert.InRange(interpreterIds.Count, 1, rows.Count - 1);
        Assert.Equal(interpreterIds, sqlIds);
    }

    [Theory]
    [InlineData("MinOk(quantity, minQuantity)", 7)]
    [InlineData("IsBig(minQuantity)", 0)]
    public async Task Rule_called_with_a_missing_optional_parameter_keeps_the_interpreter_null_semantics(string filter, int expected)
    {
        var rows = await SeedAsync();

        var (sqlIds, interpreterIds) = await RuleFilterRowsAsync(rows, filter, filter, minQuantity: null);

        Assert.Equal(expected, interpreterIds.Count);
        Assert.Equal(interpreterIds, sqlIds);
    }

    [Fact]
    public async Task Parameter_with_sql_metacharacters_passed_to_a_rule_is_a_plain_value()
    {
        // ItemsNamed keeps the items for which the rule NameIs(name, nameFilter) holds.
        const string rows = "/api/apps/RecordsApp/data-sources/ItemsNamed/rows";
        await fixture.ResetAsync();
        await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        var obrien = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "O'Brien" });

        foreach (var value in new[] { "x'); DROP TABLE \"item\"; --", "' OR 1=1 --" })
        {
            using var request = Request($"{rows}?nameFilter={Uri.EscapeDataString(value)}", HostA);
            using var response = await fixture.Client.SendAsync(request, CancellationToken);

            using var body = await ReadJsonAsync(response);
            Assert.Empty(RowIds(body));
            Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt64());
        }

        using var matching = Request($"{rows}?nameFilter={Uri.EscapeDataString("O'Brien")}", HostA);
        using var matchingResponse = await fixture.Client.SendAsync(matching, CancellationToken);

        using var matchingBody = await ReadJsonAsync(matchingResponse);
        Assert.Equal([obrien], RowIds(matchingBody));
        Assert.Equal(1, matchingBody.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Rows_endpoint_returns_only_rows_passing_the_filter_and_counts_only_them()
    {
        // ActiveItems keeps active items with a quantity above 1, sorted by name.
        var rows = await SeedAsync();
        var byName = rows.ToDictionary(row => (string)row.Values["name"]!, row => row.Id);

        using var request = Request("/api/apps/RecordsApp/data-sources/ActiveItems/rows?pageSize=1", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        using var second = Request("/api/apps/RecordsApp/data-sources/ActiveItems/rows?pageSize=1&page=2&sort=-name", HostA);
        using var secondResponse = await fixture.Client.SendAsync(second, CancellationToken);

        using var body = await ReadJsonAsync(response);
        Assert.Equal([byName["Dax"]], RowIds(body));
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt64());
        using var secondBody = await ReadJsonAsync(secondResponse);
        Assert.Equal([byName["Dax"]], RowIds(secondBody));
        Assert.Equal(2, secondBody.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Filter_the_database_cannot_evaluate_is_a_400_problem_without_sql()
    {
        // ItemsOverflow multiplies the quantity by the largest 64-bit integer.
        await fixture.ResetAsync();
        await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "3" });

        using var request = Request("/api/apps/RecordsApp/data-sources/ItemsOverflow/rows", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("The data source filter could not be evaluated for these rows.", problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.DoesNotContain("quantity", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overflow", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Filter_that_overflows_for_no_row_returns_its_rows()
    {
        await fixture.ResetAsync();
        var kept = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "1" });
        await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Chair", ["quantity"] = null });

        using var request = Request("/api/apps/RecordsApp/data-sources/ItemsOverflow/rows", HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var body = await ReadJsonAsync(response);
        Assert.Equal([kept], RowIds(body));
        Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt64());
    }

    /// <summary>
    /// The ids the SQL keeps for <paramref name="filter"/> and the ids the interpreter keeps for
    /// <paramref name="interpreterFilter"/>, both sorted. The filters can call the rules of
    /// <see cref="Rules"/> and name the optional integer parameter <c>minQuantity</c>. The
    /// interpreter also sees <c>departmentName</c>, the name of the row's department.
    /// </summary>
    private async Task<(List<Guid> Sql, List<Guid> Interpreter)> RuleFilterRowsAsync(
        List<(Guid Id, Dictionary<string, object?> Values)> rows, string filter, string interpreterFilter, long? minQuantity)
    {
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        Assert.True(item.TryGetField("name", out var nameField));
        EntityModel? FindEntity(string name) => fixture.Model.TryGetEntity(name, out var entity) ? entity : null;
        var rules = Rules();
        var departmentName = new DataSourceParameterModel("departmentName", FieldType.Text, false, null, null, null);
        var compiled = ExpressionModel.Compile(
            filter, ExpressionScopes.ForDataSource(item.Fields, [_minQuantity], FindEntity, rules), ExpressionType.Boolean);
        var interpreted = ExpressionModel.Compile(
            interpreterFilter,
            ExpressionScopes.ForDataSource(item.Fields, [_minQuantity, departmentName], FindEntity, rules),
            ExpressionType.Boolean);
        var dataSource = new DataSourceModel
        {
            Id = Guid.NewGuid(),
            Name = "Filtered",
            File = "data-sources/filtered.json",
            Entity = new EntityReference(item.Id, item.Name),
            Fields = [new DataSourceFieldModel("name", nameField)],
            Parameters = [_minQuantity],
            Filter = compiled,
        };

        var expected = new List<Guid>();
        foreach (var (id, values) in rows)
        {
            var withParameters = new Dictionary<string, object?>(values, StringComparer.Ordinal)
            {
                ["minQuantity"] = minQuantity,
                ["departmentName"] = values["department"] is null ? null : "Sales",
            };
            var evaluated = ExpressionInterpreter.Evaluate(interpreted.Syntax, interpreted.Check, new ExpressionValues(withParameters));
            Assert.True(evaluated.Succeeded, evaluated.Error?.Message);
            if (evaluated.Value is true)
            {
                expected.Add(id);
            }
        }

        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var page = await DataSourceQueries.ListAsync(
            connection, fixture.Model, dataSource, 1, 100, null, [new DataSourceParameterValue(_minQuantity, minQuantity)], CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(expected.Count, page.TotalCount);
        return (Sorted(page.Items.Select(row => row.Id!.Value)), Sorted(expected));
    }

    /// <summary>The rules the rule filters call, including one that calls another.</summary>
    private static List<ExpressionRule> Rules()
    {
        var isBig = Rule("IsBig", "value > 2", ExpressionType.Boolean, [], ("value", ExpressionType.Integer));
        return
        [
            isBig,
            Rule("AtLeast", "value >= min", ExpressionType.Boolean, [], ("value", ExpressionType.Decimal), ("min", ExpressionType.Decimal)),
            Rule("BothBig", "IsBig(a) and IsBig(b)", ExpressionType.Boolean, [isBig], ("a", ExpressionType.Integer), ("b", ExpressionType.Integer)),
            Rule("InSales", "n == 'Sales'", ExpressionType.Boolean, [], ("n", ExpressionType.Text)),
            Rule("MinOk", "min is null or q >= min", ExpressionType.Boolean, [], ("q", ExpressionType.Integer), ("min", ExpressionType.Integer)),
            Rule("Twice", "v + v", ExpressionType.Integer, [], ("v", ExpressionType.Integer)),
            Rule("Mentions", "contains(s, needle) or startsWith(s, 'B')", ExpressionType.Boolean, [], ("s", ExpressionType.Text), ("needle", ExpressionType.Text)),
        ];
    }

    private static ExpressionRule Rule(
        string name, string body, ExpressionType result, IEnumerable<ExpressionRule> rules, params (string Name, ExpressionType Type)[] parameters)
    {
        var parsed = ExpressionParser.Parse(body);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var scope = new ExpressionScope(parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Type), rules);
        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, result);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return new ExpressionRule(name, [.. parameters.Select(parameter => new ExpressionRuleParameter(parameter.Name, parameter.Type))], result)
        {
            Body = parsed.Expression,
            BodyCheck = check,
        };
    }

    /// <summary>Empties both tenants, then inserts the seed items in tenant A. Returns each item's id and its field values.</summary>
    private async Task<List<(Guid Id, Dictionary<string, object?> Values)>> SeedAsync()
    {
        await fixture.ResetAsync();
        var department = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });
        var rows = new List<(Guid, Dictionary<string, object?>)>();
        foreach (var seed in _seed)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = seed.Name,
                ["quantity"] = seed.Quantity,
                ["price"] = seed.Price,
                ["active"] = seed.Active,
                ["neededBy"] = seed.NeededBy,
                ["orderedAt"] = seed.OrderedAt,
                ["status"] = seed.Status,
                ["department"] = seed.HasDepartment ? department : null,
            };
            var id = await fixture.InsertAsync(
                TenantA,
                "Item",
                values.ToDictionary(pair => pair.Key, pair => pair.Value switch
                {
                    null => null,
                    DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
                    bool boolean => boolean ? "true" : "false",
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    var value => value.ToString(),
                }));
            rows.Add((id, values));
        }

        return rows;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> RowIds(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    private static List<Guid> Sorted(IEnumerable<Guid> ids) => ids.Order().ToList();

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private sealed record SeedItem(
        string Name,
        long? Quantity,
        decimal? Price,
        bool? Active,
        DateOnly? NeededBy,
        DateTimeOffset? OrderedAt,
        string? Status,
        bool HasDepartment);
}
