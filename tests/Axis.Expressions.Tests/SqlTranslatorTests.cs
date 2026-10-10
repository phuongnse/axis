using System.Diagnostics;
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
        // now() is PostgreSQL's transaction start time, with no parameter.
        { "ts < now()", "(\"ts\" < now())", [] },
    };

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
    [InlineData("i / 2 > 1", 2, "Operator '/'")]
    [InlineData("i > 0 and lower(t).x", 18, "Paths")]
    [InlineData("dt == date('2026-13-45')", 6, "'2026-13-45' is not a valid date")]
    [InlineData("ts == dateTime('2026-10-08')", 6, "'2026-10-08' is not a valid date-time")]
    [InlineData("b and sum(lines, i) > 0", 6, "Function 'sum'")]
    [InlineData("COUNT(lines) > 0", 0, "Function 'count'")]
    public void Construct_outside_the_subset_is_reported_at_it(string expression, int offset, string named)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded);

        var result = SqlTranslator.Translate(parsed.Expression, Column);

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

    [Fact]
    public void Rule_chain_past_the_inlined_node_cap_is_outside_the_subset_before_rendering()
    {
        var rules = Chain(8);
        var stopwatch = Stopwatch.StartNew();

        var result = TranslateWithRules("L8(i)", rules);

        stopwatch.Stop();
        Assert.False(result.Succeeded);
        Assert.Null(result.Sql);
        Assert.Empty(result.Parameters);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, 0), (result.Diagnostic.Code, result.Diagnostic.Offset));
        Assert.Contains("'L8'", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("2,000", result.Diagnostic.Message, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Took {stopwatch.Elapsed}.");
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
