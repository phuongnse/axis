using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

public sealed class SqlTranslatorTests
{
    private static readonly ExpressionScope _scope = new(new Dictionary<string, ExpressionType>
    {
        ["t"] = ExpressionType.Text,
        ["i"] = ExpressionType.Integer,
        ["d"] = ExpressionType.Decimal,
        ["b"] = ExpressionType.Boolean,
        ["dt"] = ExpressionType.Date,
        ["ts"] = ExpressionType.DateTime,
        ["e"] = ExpressionType.Enum("status", ["draft", "submitted"]),
    });

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
    [InlineData("i > 0 and t.x", 11, "Paths")]
    [InlineData("dt == date('2026-13-45')", 6, "'2026-13-45' is not a valid date")]
    [InlineData("ts == dateTime('2026-10-08')", 6, "'2026-10-08' is not a valid date-time")]
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

    /// <summary>Parses and type-checks <paramref name="expression"/> as a boolean, then translates it.</summary>
    private static SqlTranslationResult Translate(string expression)
    {
        var parsed = ExpressionParser.Parse(expression);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return SqlTranslator.Translate(parsed.Expression, Column);
    }

    private static string Column(string name) => $"\"{name}\"";
}
