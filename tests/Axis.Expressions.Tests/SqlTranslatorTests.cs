using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

public sealed class SqlTranslatorTests
{
    private static readonly Dictionary<string, ExpressionType> _fields = new()
    {
        ["t"] = ExpressionType.Text,
        ["i"] = ExpressionType.Integer,
        ["d"] = ExpressionType.Decimal,
        ["b"] = ExpressionType.Boolean,
        ["dt"] = ExpressionType.Date,
        ["ts"] = ExpressionType.DateTime,
        ["e"] = ExpressionType.Enum("status", ["draft", "submitted"]),
    };

    private static readonly ExpressionScope _scope = new(_fields);

    private static readonly DateTimeOffset _instant = new(2026, 10, 8, 2, 30, 0, TimeSpan.Zero);

    public static TheoryData<string, string, (string, ExpressionTypeKind, object)[]> Translations => new()
    {
        // Literals: each value is a typed parameter, and null is the keyword.
        { "i == 1", "(\"i\" IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Integer, 1L)] },
        { "d == 1.50", "(\"d\" IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Decimal, 1.50m)] },
        { "t == 'it''s'", "(\"t\" IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Text, "it's")] },
        { "b == true", "(\"b\" IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Boolean, true)] },
        { "null == t", "(NULL IS NOT DISTINCT FROM \"t\")", [] },
        { "dt == date('2026-10-08')", "(\"dt\" IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Date, new DateOnly(2026, 10, 8))] },
        { "ts == DATETIME('2026-10-08T09:30:00+07:00')", "(\"ts\" IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.DateTime, _instant)] },
        // Comparison operators.
        { "t != 'a'", "(\"t\" IS DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Text, "a")] },
        { "i < 1", "(\"i\" < @f0)", [("f0", ExpressionTypeKind.Integer, 1L)] },
        { "i <= 1", "(\"i\" <= @f0)", [("f0", ExpressionTypeKind.Integer, 1L)] },
        { "i > 1", "(\"i\" > @f0)", [("f0", ExpressionTypeKind.Integer, 1L)] },
        { "i >= d", "(\"i\" >= \"d\")", [] },
        // Arithmetic operators and unary minus.
        { "i + 1 > 2", "((\"i\" + @f0) > @f1)", [("f0", ExpressionTypeKind.Integer, 1L), ("f1", ExpressionTypeKind.Integer, 2L)] },
        { "i - d > 0", "((\"i\" - \"d\") > @f0)", [("f0", ExpressionTypeKind.Integer, 0L)] },
        { "i * 2 > 0", "((\"i\" * @f0) > @f1)", [("f0", ExpressionTypeKind.Integer, 2L), ("f1", ExpressionTypeKind.Integer, 0L)] },
        { "-i < -1", "((-\"i\") < (-@f0))", [("f0", ExpressionTypeKind.Integer, 1L)] },
        { "- -i > 0", "((-(-\"i\")) > @f0)", [("f0", ExpressionTypeKind.Integer, 0L)] },
        // Logic keeps the expression's grouping.
        { "b and not b", "(\"b\" AND (NOT \"b\"))", [] },
        { "b or b and b", "(\"b\" OR (\"b\" AND \"b\"))", [] },
        { "(b or b) and b", "((\"b\" OR \"b\") AND \"b\")", [] },
        // is null, is not null and in.
        { "t is null", "(\"t\" IS NULL)", [] },
        { "t is not null", "(\"t\" IS NOT NULL)", [] },
        {
            "e in ('draft', 'submitted')",
            "(\"e\" IS NOT NULL AND \"e\" IN (@f0, @f1))",
            [("f0", ExpressionTypeKind.Text, "draft"), ("f1", ExpressionTypeKind.Text, "submitted")]
        },
        {
            "i + 1 in (2, 3.5)",
            "((\"i\" + @f0) IS NOT NULL AND (\"i\" + @f0) IN (@f1, @f2))",
            [("f0", ExpressionTypeKind.Integer, 1L), ("f1", ExpressionTypeKind.Integer, 2L), ("f2", ExpressionTypeKind.Decimal, 3.5m)]
        },
        { "dt in (date('2026-10-08'))", "(\"dt\" IS NOT NULL AND \"dt\" IN (@f0))", [("f0", ExpressionTypeKind.Date, new DateOnly(2026, 10, 8))] },
        // Functions.
        { "if(b, i, 0) > 1", "((CASE WHEN \"b\" THEN \"i\" ELSE @f0 END) > @f1)", [("f0", ExpressionTypeKind.Integer, 0L), ("f1", ExpressionTypeKind.Integer, 1L)] },
        { "coalesce(t, null, 'x') == 'x'", "(COALESCE(\"t\", NULL, @f0) IS NOT DISTINCT FROM @f1)", [("f0", ExpressionTypeKind.Text, "x"), ("f1", ExpressionTypeKind.Text, "x")] },
        { "length(concat(t, 'zz')) > 3", "(char_length(concat(\"t\", @f0)) > @f1)", [("f0", ExpressionTypeKind.Text, "zz"), ("f1", ExpressionTypeKind.Integer, 3L)] },
        { "contains(t, 'a')", "(strpos(\"t\", @f0) > 0)", [("f0", ExpressionTypeKind.Text, "a")] },
        { "startsWith(t, 'a')", "starts_with(\"t\", @f0)", [("f0", ExpressionTypeKind.Text, "a")] },
        { "endsWith(t, 'a')", "(right(\"t\", char_length(@f0)) = @f0)", [("f0", ExpressionTypeKind.Text, "a")] },
        { "abs(i) == 1", "(abs(\"i\") IS NOT DISTINCT FROM @f0)", [("f0", ExpressionTypeKind.Integer, 1L)] },
        { "floor(i) == ceiling(d)", "(floor((\"i\")::numeric) IS NOT DISTINCT FROM ceil((\"d\")::numeric))", [] },
        { "round(d, 1) == 1.5", "(round((\"d\")::numeric, (@f0)::int) IS NOT DISTINCT FROM @f1)", [("f0", ExpressionTypeKind.Integer, 1L), ("f1", ExpressionTypeKind.Decimal, 1.5m)] },
        {
            "year(dt) == month(dt) + day(dt)",
            "(((extract(year from \"dt\"))::bigint) IS NOT DISTINCT FROM (((extract(month from \"dt\"))::bigint) + ((extract(day from \"dt\"))::bigint)))",
            []
        },
        { "addDays(dt, 3) > dt", "((\"dt\" + (@f0)::int) > \"dt\")", [("f0", ExpressionTypeKind.Integer, 3L)] },
        { "daysBetween(dt, date('2026-10-08')) == 1", "((@f0 - \"dt\") IS NOT DISTINCT FROM @f1)", [("f0", ExpressionTypeKind.Date, new DateOnly(2026, 10, 8)), ("f1", ExpressionTypeKind.Integer, 1L)] },
        // now() is the scenario's axis.now setting, or PostgreSQL's transaction start time, with no parameter.
        { "ts < now()", $"(\"ts\" < {SqlTranslator.NowSql})", [] },
    };

    [Fact]
    public void Today_converts_the_session_time_with_the_time_zone_id_as_a_text_parameter()
    {
        var result = Translate("today() == dt", TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal($"((({SqlTranslator.NowSql}) AT TIME ZONE @f0)::date IS NOT DISTINCT FROM \"dt\")", result.Sql);
        var parameter = Assert.Single(result.Parameters);
        Assert.Equal(("f0", ExpressionTypeKind.Text, "Asia/Ho_Chi_Minh"), (parameter.Name, parameter.Kind, parameter.Value));
        Assert.DoesNotContain("Asia", result.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void To_date_casts_its_argument_to_an_instant_and_converts_it_with_the_time_zone()
    {
        var result = Translate("toDate(ts) == dt", TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal("((((\"ts\")::timestamptz) AT TIME ZONE @f0)::date IS NOT DISTINCT FROM \"dt\")", result.Sql);
        var parameter = Assert.Single(result.Parameters);
        Assert.Equal(("f0", ExpressionTypeKind.Text, "Asia/Ho_Chi_Minh"), (parameter.Name, parameter.Kind, parameter.Value));
    }

    [Fact]
    public void To_date_of_a_bare_null_keeps_the_cast_so_the_argument_has_a_type()
    {
        var result = Translate("toDate(null) is null", null);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Contains("((NULL)::timestamptz)", result.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_time_zone_today_and_to_date_use_utc()
    {
        var today = Translate("today() == dt", null);
        var toDate = Translate("toDate(ts) == dt", null);

        Assert.Equal("UTC", Assert.Single(today.Parameters).Value);
        Assert.Equal("UTC", Assert.Single(toDate.Parameters).Value);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Expression_in_the_subset_renders_the_documented_sql_with_typed_parameters(
        string expression, string expectedSql, (string, ExpressionTypeKind, object)[] expectedParameters)
    {
        var result = Translate(expression);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expectedSql, result.Sql);
        Assert.Equal(expectedParameters, result.Parameters.Select(parameter => (parameter.Name, parameter.Kind, parameter.Value)));
    }

    [Fact]
    public void Current_user_in_a_policy_filter_is_the_named_parameter_the_caller_binds()
    {
        var parsed = ExpressionParser.Parse("t == currentUser() or t == 'x'");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean, policyFilter: true);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var result = SqlTranslator.Translate(parsed.Expression, Column, check);

        // Only the literal is a numbered parameter. The user id is never part of the SQL text.
        Assert.Equal("((\"t\" IS NOT DISTINCT FROM @currentUser) OR (\"t\" IS NOT DISTINCT FROM @f0))", result.Sql);
        Assert.Equal("f0", Assert.Single(result.Parameters).Name);
        Assert.Equal("currentUser", SqlTranslator.CurrentUserParameter);
    }

    [Fact]
    public void A_parameter_prefix_names_every_literal_parameter()
    {
        var parsed = ExpressionParser.Parse("i == 1 or t == 'x'");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = SqlTranslator.Translate(parsed.Expression, Column, parameterPrefix: "g2f");

        Assert.Equal("((\"i\" IS NOT DISTINCT FROM @g2f0) OR (\"t\" IS NOT DISTINCT FROM @g2f1))", result.Sql);
        Assert.Equal(["g2f0", "g2f1"], result.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void Text_and_names_never_reach_the_sql_unquoted()
    {
        var result = Translate("t == 'x''); DROP TABLE t; --'");

        Assert.Equal("(\"t\" IS NOT DISTINCT FROM @f0)", result.Sql);
        Assert.Equal("x'); DROP TABLE t; --", Assert.Single(result.Parameters).Value);
    }

    [Theory]
    [InlineData("lower(t) == 'a'", 0, "Function 'lower'")]
    [InlineData("UPPER(t) == 'a'", 0, "Function 'upper'")]
    [InlineData("b and trim(t) == 'a'", 6, "Function 'trim'")]
    [InlineData("previous(i) == i", 0, "Function 'previous'")]
    [InlineData("i / 2 > 1", 2, "Operator '/'")]
    [InlineData("i > 0 and lower(t).x", 18, "Paths")]
    [InlineData("dt == date('2026-13-45')", 6, "'2026-13-45' is not a valid date")]
    [InlineData("ts == dateTime('2026-10-08')", 6, "'2026-10-08' is not a valid date-time")]
    [InlineData("b and sum(lines, i) > 0", 6, "Function 'sum'")]
    [InlineData("SUM(lines, qty) > 0", 0, "Function 'sum'")]
    [InlineData("avg(lines, qty) > 0", 0, "Function 'avg'")]
    [InlineData("min(lines, qty) > 0", 0, "Function 'min'")]
    [InlineData("max(lines, qty) > 0", 0, "Function 'max'")]
    [InlineData("all(lines, ok)", 0, "Function 'all'")]
    [InlineData("first(lines, ok, qty) > 0", 0, "Function 'first'")]
    [InlineData("any(lines, lower(name) == 'a')", 11, "Function 'lower'")]
    [InlineData("b and count(lines, trim(name) == 'a') > 0", 19, "Function 'trim'")]
    public void Construct_outside_the_subset_is_reported_at_it(string expression, int offset, string named)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column, collection: Lines);

        Assert.False(result.Succeeded);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, offset), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains(named, result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.EndsWith($"at character {offset + 1}.", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_is_given_to_the_column_callback_as_its_names()
    {
        var parsed = ExpressionParser.Parse("department.manager.name == 'a' and i > 0");
        Assert.True(parsed.Succeeded);
        var paths = new List<string>();

        var result = SqlTranslator.Translate(parsed.Expression, path =>
        {
            paths.Add(string.Join('|', path));
            return Column(path);
        });

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(["department|manager|name", "i"], paths);
        Assert.Equal("((\"department.manager.name\" IS NOT DISTINCT FROM @f0) AND (\"i\" > @f1))", result.Sql);
    }

    private static readonly ExpressionScope _itemScope = new(
        new Dictionary<string, ExpressionType>
        {
            ["qty"] = ExpressionType.Integer,
            ["ok"] = ExpressionType.Boolean,
            ["name"] = ExpressionType.Text,
            ["product"] = ExpressionType.Reference("Product"),
        },
        referenceFields: (target, name) => target == "Product" && name == "name" ? ExpressionType.Text : null);

    private static readonly ExpressionScope _collectionScope = new(
        _fields, collections: [new ExpressionCollection("lines", "Line", _itemScope)]);

    private static SqlCollection Lines(string name) =>
        name == "lines"
            ? new SqlCollection("\"line\" AS \"a0\"", "\"a0\".\"owner_id\" = \"r\".\"id\"", path => $"\"a0\".\"{string.Join('.', path)}\"")
            : throw new InvalidOperationException(name);

    public static TheoryData<string, string, (string, ExpressionTypeKind, object)[]> Aggregates => new()
    {
        {
            "any(lines, ok)",
            "EXISTS (SELECT 1 FROM \"line\" AS \"a0\" WHERE \"a0\".\"owner_id\" = \"r\".\"id\" AND (\"a0\".\"ok\"))",
            []
        },
        { "count(lines) > 1", "((SELECT count(*) FROM \"line\" AS \"a0\" WHERE \"a0\".\"owner_id\" = \"r\".\"id\") > @f0)", [("f0", ExpressionTypeKind.Integer, 1L)] },
        {
            "count(lines, qty > 2) >= 2",
            "((SELECT count(*) FROM \"line\" AS \"a0\" WHERE \"a0\".\"owner_id\" = \"r\".\"id\" AND ((\"a0\".\"qty\" > @f0))) >= @f1)",
            [("f0", ExpressionTypeKind.Integer, 2L), ("f1", ExpressionTypeKind.Integer, 2L)]
        },
        {
            "any(lines, product.name == 'Desk')",
            "EXISTS (SELECT 1 FROM \"line\" AS \"a0\" WHERE \"a0\".\"owner_id\" = \"r\".\"id\" AND ((\"a0\".\"product.name\" IS NOT DISTINCT FROM @f0)))",
            [("f0", ExpressionTypeKind.Text, "Desk")]
        },
        // The names outside the aggregate are still the filter's own columns.
        {
            "b and any(lines, ok) and i > 0",
            "((\"b\" AND EXISTS (SELECT 1 FROM \"line\" AS \"a0\" WHERE \"a0\".\"owner_id\" = \"r\".\"id\" AND (\"a0\".\"ok\"))) AND (\"i\" > @f0))",
            [("f0", ExpressionTypeKind.Integer, 0L)]
        },
        // A null condition is never true, so the subquery keeps no row.
        { "any(lines, null)", "EXISTS (SELECT 1 FROM \"line\" AS \"a0\" WHERE \"a0\".\"owner_id\" = \"r\".\"id\" AND (NULL))", [] },
    };

    [Theory]
    [MemberData(nameof(Aggregates))]
    public void Any_and_count_render_a_subquery_over_the_collections_source(
        string expression, string expectedSql, (string, ExpressionTypeKind, object)[] expectedParameters)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _collectionScope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var result = SqlTranslator.Translate(parsed.Expression, Column, check, collection: Lines);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expectedSql, result.Sql);
        Assert.Equal(expectedParameters, result.Parameters.Select(parameter => (parameter.Name, parameter.Kind, parameter.Value)));
    }

    [Theory]
    [InlineData("any(lines, ok)", "any")]
    [InlineData("count(lines) > 1", "count")]
    public void Any_and_count_are_outside_the_subset_without_a_collection_callback(string expression, string named)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column);

        Assert.False(result.Succeeded);
        Assert.Equal(ExpressionDiagnosticCodes.OutsideSqlSubset, result.Diagnostic.Code);
        Assert.Contains($"Function '{named}'", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_inside_an_aggregate_is_given_to_the_collections_column_callback_as_its_names()
    {
        var parsed = ExpressionParser.Parse("i > 0 and any(lines, product.name == 'Desk' and qty > 1)");
        Assert.True(parsed.Succeeded);
        var outer = new List<string>();
        var inner = new List<string>();
        var requested = new List<string>();

        var result = SqlTranslator.Translate(
            parsed.Expression,
            path =>
            {
                outer.Add(string.Join('|', path));
                return Column(path);
            },
            collection: name =>
            {
                requested.Add(name);
                return new SqlCollection("line", "TRUE", path =>
                {
                    inner.Add(string.Join('|', path));
                    return Column(path);
                });
            });

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(["i"], outer);
        Assert.Equal(["lines"], requested);
        Assert.Equal(["product|name", "qty"], inner);
    }

    [Theory]
    [InlineData("any(lines, qty > outer.i)")]
    [InlineData("count(lines, OUTER.i == qty) > 0")]
    [InlineData("any(lines, outer.i.name == 'x')")]
    public void Outer_inside_an_aggregate_is_outside_the_subset(string expression)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column, collection: Lines);

        Assert.False(result.Succeeded);
        Assert.Equal(ExpressionDiagnosticCodes.OutsideSqlSubset, result.Diagnostic.Code);
        Assert.Contains("'outer' is not translated to SQL", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_inside_an_aggregate_is_reported_at_it()
    {
        var parsed = ExpressionParser.Parse("any(lines, any(lines, ok))");
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column, collection: Lines);

        Assert.False(result.Succeeded);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 11), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("top level", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, (string, ExpressionTypeKind, object)[]> RuleCalls => new()
    {
        // The body replaces the call, with each parameter replaced by its argument.
        { "IsBig(i)", "((\"i\" > @f0))", [("f0", ExpressionTypeKind.Integer, 10L)] },
        // An argument for a decimal parameter is cast to numeric, as the interpreter widens it.
        {
            "AtLeast(i, 1)",
            "(((\"i\")::numeric >= (@f0)::numeric))",
            [("f0", ExpressionTypeKind.Integer, 1L)]
        },
        // A parameter used twice repeats the argument's SQL. A literal argument is one parameter.
        { "Twice(i) > 2", "(((\"i\" + \"i\")) > @f0)", [("f0", ExpressionTypeKind.Integer, 2L)] },
        { "Twice(5) > i", "(((@f0 + @f0)) > \"i\")", [("f0", ExpressionTypeKind.Integer, 5L)] },
        // The parameter t shadows the field t inside the body.
        { "Len('abc') > 2", "((char_length(@f0)) > @f1)", [("f0", ExpressionTypeKind.Text, "abc"), ("f1", ExpressionTypeKind.Integer, 2L)] },
        // A rule that calls a rule inlines both. The arguments are rendered before the body.
        {
            "BothBig(i, 3)",
            "((((\"i\" > @f1)) AND ((@f0 > @f2))))",
            [("f0", ExpressionTypeKind.Integer, 3L), ("f1", ExpressionTypeKind.Integer, 10L), ("f2", ExpressionTypeKind.Integer, 10L)]
        },
        // A decimal result is cast to numeric.
        { "Half(i) > 1", "(((((\"i\")::numeric * @f0))::numeric) > @f1)", [("f0", ExpressionTypeKind.Decimal, 0.5m), ("f1", ExpressionTypeKind.Integer, 1L)] },
    };

    [Theory]
    [MemberData(nameof(RuleCalls))]
    public void Rule_call_renders_the_inlined_body_with_each_parameter_replaced_by_its_argument(
        string expression, string expectedSql, (string, ExpressionTypeKind, object)[] expectedParameters)
    {
        var result = TranslateWithRules(expression, SampleRules());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expectedSql, result.Sql);
        Assert.Equal(expectedParameters, result.Parameters.Select(parameter => (parameter.Name, parameter.Kind, parameter.Value)));
    }

    [Fact]
    public void Rule_body_outside_the_subset_is_reported_at_the_call_naming_the_rule()
    {
        var isLower = Rule("IsLower", "lower(value) == value", ExpressionType.Boolean, [], ("value", ExpressionType.Text));

        var result = TranslateWithRules("b and IsLower(t)", [isLower]);

        Assert.False(result.Succeeded);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 6), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("'IsLower'", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'lower'", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.EndsWith("at character 7.", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Qualified_call_inlines_the_module_rule_and_the_module_rule_it_calls()
    {
        // A module rule calls the rules of its own module by their bare names, so only the called one carries the module.
        var inner = Rule("Inner", "value < 100", ExpressionType.Boolean, [], ("value", ExpressionType.Integer));
        var outer = Rule("Outer", "value > 0 and Inner(value)", ExpressionType.Boolean, [inner], ("value", ExpressionType.Integer)) with { Module = "departments" };
        var parsed = ExpressionParser.Parse("departments:Outer(i)");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, new ExpressionScope(_fields, [outer], modules: ["departments"]), ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var result = SqlTranslator.Translate(parsed.Expression, Column, check);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Contains("\"i\" > @f0", result.Sql, StringComparison.Ordinal);
        Assert.Contains("\"i\" < @f1", result.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Qualified_call_without_a_resolved_rule_is_never_a_built_in_function()
    {
        var parsed = ExpressionParser.Parse("departments:abs(i) == 1");
        var other = ExpressionParser.Parse("i == 1");
        Assert.True(parsed.Succeeded && other.Succeeded);
        var check = ExpressionTypeChecker.Check(other.Expression, _scope, ExpressionType.Boolean);

        var result = SqlTranslator.Translate(parsed.Expression, Column, check);

        Assert.False(result.Succeeded);
        Assert.Contains("'departments:abs'", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_known_by_its_signature_only_is_outside_the_subset()
    {
        var signatureOnly = new ExpressionRule("Broken", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean);

        var result = TranslateWithRules("Broken(i)", [signatureOnly]);

        Assert.False(result.Succeeded);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 0), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("'Broken'", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_call_without_a_check_result_is_not_translated()
    {
        var parsed = ExpressionParser.Parse("IsBig(i)");
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column);

        Assert.False(result.Succeeded);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 0), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.StartsWith("Function 'IsBig' is not translated to SQL", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abs()", "Function 'abs' needs 1 argument, found 0 at character 1.")]
    [InlineData("round(1)", "Function 'round' needs 2 arguments, found 1 at character 1.")]
    public void Wrong_argument_count_without_a_check_result_is_reported_as_one(string expression, string message)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column);

        Assert.False(result.Succeeded);
        Assert.Equal((ExpressionDiagnosticCodes.WrongArgumentCount, 0, message), (result.Diagnostic.Code, result.Diagnostic.Offset, result.Diagnostic.Message));
    }

    [Fact]
    public void Rule_chain_past_the_inlined_node_cap_is_outside_the_subset_before_rendering()
    {
        var parsed = ExpressionParser.Parse("L8(i)");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, new ExpressionScope(_fields, Chain(8)), ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        var columns = 0;

        var (result, nodes) = SqlTranslator.TranslateCountingNodes(
            parsed.Expression,
            path =>
            {
                columns++;
                return Column(path);
            },
            check);

        // The count stops at the first node past the cap, and no column is rendered before that.
        Assert.Equal(ExpressionLimits.MaxInlinedNodes + 1, nodes);
        Assert.Equal(0, columns);
        Assert.False(result.Succeeded);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 0), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("'L8'", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("2,000", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    // L3's body inlines to 423 nodes and L4's to 1,703, so L4(i) is 1,705 with its call and argument.
    [InlineData("L3(i)")]
    [InlineData("L4(i)")]
    public void Rule_chain_under_the_inlined_node_cap_translates(string expression)
    {
        var result = TranslateWithRules(expression, Chain(4));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Fact]
    public void Inlined_node_cap_names_the_rule_the_filter_calls()
    {
        var chain = Chain(5);
        var outer = Rule("Outer", "L5(value)", ExpressionType.Boolean, chain, ("value", ExpressionType.Integer));

        var result = TranslateWithRules("b and Outer(i)", [outer]);

        Assert.False(result.Succeeded);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 6), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("'Outer'", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'L", result.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_nodes_after_a_large_rule_call_that_pass_the_cap_name_that_rule()
    {
        // L4(i) inlines to 1,705 nodes. With "and", "in" and "i" the filter reaches 1,708 before the
        // list, and each item adds one, so item 293 passes 2,000 outside any rule body.
        static string Filter(int items) => $"L4(i) and i in ({string.Join(", ", Enumerable.Range(1, items))})";

        var result = TranslateWithRules(Filter(293), Chain(4));

        Assert.False(result.Succeeded);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 0), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("'L4'", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("2,000", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.True(TranslateWithRules(Filter(292), Chain(4)).Succeeded);
    }

    /// <summary>
    /// <c>L0(value) = value &gt; 0</c>, and each <c>Lk(value)</c> up to <paramref name="depth"/>
    /// calls <c>L(k-1)(value)</c> four times. The last one comes first.
    /// </summary>
    private static List<ExpressionRule> Chain(int depth)
    {
        var rules = new List<ExpressionRule> { Rule("L0", "value > 0", ExpressionType.Boolean, [], ("value", ExpressionType.Integer)) };
        for (var k = 1; k <= depth; k++)
        {
            var call = $"L{k - 1}(value)";
            rules.Insert(0, Rule($"L{k}", $"{call} and {call} and {call} and {call}", ExpressionType.Boolean, rules, ("value", ExpressionType.Integer)));
        }

        return rules;
    }

    private static List<ExpressionRule> SampleRules()
    {
        var isBig = Rule("IsBig", "value > 10", ExpressionType.Boolean, [], ("value", ExpressionType.Integer));
        return
        [
            isBig,
            Rule("AtLeast", "value >= min", ExpressionType.Boolean, [], ("value", ExpressionType.Decimal), ("min", ExpressionType.Decimal)),
            Rule("Twice", "value + value", ExpressionType.Integer, [], ("value", ExpressionType.Integer)),
            Rule("Len", "length(t)", ExpressionType.Integer, [], ("t", ExpressionType.Text)),
            Rule("BothBig", "IsBig(a) and IsBig(b)", ExpressionType.Boolean, [isBig], ("a", ExpressionType.Integer), ("b", ExpressionType.Integer)),
            Rule("Half", "value * 0.5", ExpressionType.Decimal, [], ("value", ExpressionType.Decimal)),
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

    private const string LookupColumn = "r.f_t_lookup";

    [Theory]
    [InlineData("t == 'x'", "(r.f_t_lookup IS NOT NULL AND r.f_t_lookup = ANY(@fl0))")]
    [InlineData("'x' == t", "(r.f_t_lookup IS NOT NULL AND r.f_t_lookup = ANY(@fl0))")]
    [InlineData("t != 'x'", "(r.f_t_lookup IS NULL OR NOT (r.f_t_lookup = ANY(@fl0)))")]
    [InlineData("'x' != t", "(r.f_t_lookup IS NULL OR NOT (r.f_t_lookup = ANY(@fl0)))")]
    [InlineData("not (t == 'x')", "(NOT (r.f_t_lookup IS NOT NULL AND r.f_t_lookup = ANY(@fl0)))")]
    public void An_equality_on_a_lookup_field_renders_against_the_lookup_column_with_one_lookup_and_no_literal(string expression, string sql)
    {
        var result = TranslateWithLookup(expression);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(sql, result.Sql);
        Assert.Empty(result.Parameters);
        var lookup = Assert.Single(result.Lookups);
        Assert.Equal(("fl0", ExpressionTypeKind.Text, "x"), (lookup.Name, lookup.Kind, lookup.Value));
        Assert.Equal(["t"], lookup.Path);
    }

    [Fact]
    public void A_lookup_keeps_the_prefix_and_numbers_each_lookup_apart_from_the_literal_parameters()
    {
        var parsed = ExpressionParser.Parse("t == 'x' and i == 3 and t != 'y'");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = SqlTranslator.Translate(
            parsed.Expression, Column, parameterPrefix: "g0f", lookupColumn: path => path is ["t"] ? LookupColumn : null);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(["g0fl0", "g0fl1"], result.Lookups.Select(lookup => lookup.Name));
        var parameter = Assert.Single(result.Parameters);
        Assert.Equal(("g0f0", 3L), (parameter.Name, parameter.Value));
        Assert.Contains("@g0fl1", result.Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("i == 3")]
    [InlineData("t == null")]
    [InlineData("t is null")]
    [InlineData("t == t")]
    [InlineData("length(t) == 3")]
    public void Anything_else_on_a_lookup_field_or_a_field_without_a_lookup_column_renders_as_before(string expression)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var plain = SqlTranslator.Translate(parsed.Expression, Column);
        var withCallback = SqlTranslator.Translate(parsed.Expression, Column, lookupColumn: path => path is ["t"] ? LookupColumn : null);

        Assert.Equal(plain.Sql, withCallback.Sql);
        Assert.Empty(withCallback.Lookups);
    }

    private const string ParameterEqual =
        "((@p0 IS NULL AND r.f_t_lookup IS NULL) OR (@p0 IS NOT NULL AND r.f_t_lookup IS NOT NULL AND r.f_t_lookup = ANY(@fl0)))";

    [Theory]
    [InlineData("t == p", ParameterEqual)]
    [InlineData("p == t", ParameterEqual)]
    [InlineData("t != p", "(NOT " + ParameterEqual + ")")]
    [InlineData("not (t == p)", "(NOT " + ParameterEqual + ")")]
    public void An_equality_of_a_lookup_field_and_a_parameter_names_the_parameter_and_has_no_literal(string expression, string sql)
    {
        var result = TranslateWithParameter(expression);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(sql, result.Sql);
        Assert.Empty(result.Parameters);
        var lookup = Assert.Single(result.Lookups);
        Assert.Equal(("fl0", "p", null), (lookup.Name, lookup.Parameter, lookup.Value));
        Assert.Equal(["t"], lookup.Path);
    }

    [Fact]
    public void A_literal_lookup_has_no_parameter_name()
    {
        var lookup = Assert.Single(TranslateWithLookup("t == 'x'").Lookups);

        Assert.Null(lookup.Parameter);
    }

    [Fact]
    public void Without_a_lookup_callback_an_equality_on_the_field_renders_as_before()
    {
        var result = Translate("t == 'x'");

        Assert.Equal("(\"t\" IS NOT DISTINCT FROM @f0)", result.Sql);
        Assert.Empty(result.Lookups);
    }

    private static SqlTranslationResult TranslateWithParameter(string expression)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var scope = new ExpressionScope(new Dictionary<string, ExpressionType>(_fields) { ["p"] = ExpressionType.Text });
        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return SqlTranslator.Translate(
            parsed.Expression,
            path => path is ["p"] ? "@p0" : Column(path),
            lookupColumn: path => path is ["t"] ? LookupColumn : null);
    }

    private static SqlTranslationResult TranslateWithLookup(string expression)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return SqlTranslator.Translate(parsed.Expression, Column, lookupColumn: path => path is ["t"] ? LookupColumn : null);
    }

    /// <summary>Parses and type-checks <paramref name="expression"/> as a boolean over the fields and <paramref name="rules"/>, then translates it.</summary>
    private static SqlTranslationResult TranslateWithRules(string expression, IEnumerable<ExpressionRule> rules)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, new ExpressionScope(_fields, rules), ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return SqlTranslator.Translate(parsed.Expression, Column, check);
    }

    /// <summary>Parses and type-checks <paramref name="expression"/> as a boolean, then translates it.</summary>
    private static SqlTranslationResult Translate(string expression, TimeZoneInfo? timeZone)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return SqlTranslator.Translate(parsed.Expression, Column, timeZone: timeZone);
    }

    private static SqlTranslationResult Translate(string expression)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return SqlTranslator.Translate(parsed.Expression, Column);
    }

    private static string Column(IReadOnlyList<string> path) => $"\"{string.Join('.', path)}\"";
}
