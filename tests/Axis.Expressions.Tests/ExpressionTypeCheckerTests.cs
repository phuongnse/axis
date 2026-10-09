using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

public sealed class ExpressionTypeCheckerTests
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
        ["e2"] = ExpressionType.Enum("priority", ["low"]),
        ["r"] = ExpressionType.Reference("department"),
        ["r2"] = ExpressionType.Reference("supplier"),
    });

    public static TheoryData<string> Fields => ["t", "i", "d", "b", "dt", "ts", "e", "r"];

    [Theory]
    // Literals and names.
    [InlineData("1", "integer")]
    [InlineData("1.5", "decimal")]
    [InlineData("'a'", "text")]
    [InlineData("true", "boolean")]
    [InlineData("null", "null")]
    [InlineData("t", "text")]
    [InlineData("e", "enum of 'status'")]
    [InlineData("r", "reference to 'department'")]
    [InlineData("date('2026-10-08')", "date")]
    [InlineData("DATETIME('2026-10-08T09:30:00Z')", "date-time")]
    // Arithmetic: integers stay integers, a decimal on either side gives a decimal.
    [InlineData("i + i", "integer")]
    [InlineData("i - 1", "integer")]
    [InlineData("i * i", "integer")]
    [InlineData("i + d", "decimal")]
    [InlineData("d - i", "decimal")]
    [InlineData("1.5 * i", "decimal")]
    // Division always gives a decimal.
    [InlineData("i / i", "decimal")]
    [InlineData("d / d", "decimal")]
    // Unary minus keeps the type.
    [InlineData("-i", "integer")]
    [InlineData("-d", "decimal")]
    // Ordering on numbers, dates and date-times.
    [InlineData("i < d", "boolean")]
    [InlineData("d <= 1", "boolean")]
    [InlineData("i > 0", "boolean")]
    [InlineData("dt >= dt", "boolean")]
    [InlineData("dt < date('2026-10-08')", "boolean")]
    [InlineData("ts > dateTime('2026-10-08T09:30:00Z')", "boolean")]
    // Equality on two values of the same type, with integers widening to decimals.
    [InlineData("t == 'a'", "boolean")]
    [InlineData("i == d", "boolean")]
    [InlineData("b != true", "boolean")]
    [InlineData("dt == dt", "boolean")]
    [InlineData("ts != ts", "boolean")]
    [InlineData("e == e", "boolean")]
    [InlineData("e == 'draft'", "boolean")]
    [InlineData("'submitted' != e", "boolean")]
    [InlineData("r == r", "boolean")]
    // Booleans.
    [InlineData("b and b", "boolean")]
    [InlineData("b or i > 0", "boolean")]
    [InlineData("not b", "boolean")]
    // Membership.
    [InlineData("i in (1, 2.5)", "boolean")]
    [InlineData("d in (1)", "boolean")]
    [InlineData("t in ('a', 'b')", "boolean")]
    [InlineData("e in ('draft', 'submitted')", "boolean")]
    [InlineData("dt in (date('2026-10-08'))", "boolean")]
    [InlineData("b in (true)", "boolean")]
    public void Well_typed_expressions_have_the_expected_type(string text, string expected)
    {
        // Every type fits an expected null, so this only infers the type.
        var result = Check(text, ExpressionType.Null);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, result.Type.ToString());
    }

    [Theory]
    // Arithmetic needs numbers. Text is not joined with '+'.
    [InlineData("t + t", 2)]
    [InlineData("t + 1", 2)]
    [InlineData("i - b", 2)]
    [InlineData("dt * 2", 3)]
    [InlineData("e / 1", 2)]
    [InlineData("-t", 0)]
    [InlineData("-b", 0)]
    // Ordering works only on numbers, dates and date-times of one kind.
    [InlineData("t < t", 2)]
    [InlineData("b < b", 2)]
    [InlineData("e < e", 2)]
    [InlineData("r < r", 2)]
    [InlineData("dt <= ts", 3)]
    [InlineData("i > dt", 2)]
    [InlineData("t >= null", 2)]
    // Equality needs two values of the same type.
    [InlineData("dt == ts", 3)]
    [InlineData("i == t", 2)]
    [InlineData("b != 1", 2)]
    [InlineData("e == e2", 2)]
    [InlineData("e == t", 2)]
    [InlineData("t == e", 2)]
    [InlineData("e == 1", 2)]
    [InlineData("r == r2", 2)]
    [InlineData("r == 'a'", 2)]
    // Booleans need booleans.
    [InlineData("i and b", 2)]
    [InlineData("b or t", 2)]
    [InlineData("not i", 0)]
    // Every 'in' item fits the operand under the rules of '=='.
    [InlineData("i in ('a')", 2)]
    [InlineData("t in (1)", 2)]
    [InlineData("b in (true, 1)", 2)]
    [InlineData("dt in (dateTime('2026-10-08T09:30:00Z'))", 3)]
    // 'date' and 'dateTime' need one text literal.
    [InlineData("date(t)", 0)]
    [InlineData("dateTime(1)", 0)]
    public void Operand_types_that_do_not_fit_are_reported_at_the_operator(string text, int offset)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null);

        Assert.Equal(ExpressionDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.EndsWith($"at character {offset + 1}.", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Operand_messages_name_the_operator_and_the_types()
    {
        Assert.Equal("Operator '<' cannot compare text and text at character 3.", CheckFails("t < t", ExpressionType.Null).Message);
        Assert.Equal("Operator 'and' needs boolean, found integer at character 3.", CheckFails("i and b", ExpressionType.Null).Message);
        Assert.Equal("Operator '+' cannot combine text and integer at character 3.", CheckFails("t + 1", ExpressionType.Null).Message);
    }

    [Fact]
    public void An_unknown_field_is_reported_by_name()
    {
        var diagnostic = CheckFails("amount > 0", ExpressionType.Boolean);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, diagnostic.Code);
        Assert.Equal(0, diagnostic.Offset);
        Assert.Contains("'amount'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("date('a', 'b')", "Function 'date' needs 1 argument, found 2 at character 1.")]
    [InlineData("dateTime()", "Function 'dateTime' needs 1 argument, found 0 at character 1.")]
    public void A_date_literal_with_the_wrong_argument_count_is_reported(string text, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null);

        Assert.Equal(ExpressionDiagnosticCodes.WrongArgumentCount, diagnostic.Code);
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("r.name == 'x'", 1, "'name'")]
    [InlineData("i + missing", 4, "'missing'")]
    public void Paths_that_are_not_built_are_unknown_names(string text, int offset, string name)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Contains(name, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_names_match_ignoring_letter_case()
    {
        var result = Check("I + 1 > D and E == 'draft'", ExpressionType.Boolean);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Fact]
    public void A_result_type_mismatch_names_the_expected_and_actual_types()
    {
        var scope = new ExpressionScope(new Dictionary<string, ExpressionType> { ["quantity"] = ExpressionType.Integer });

        var diagnostic = CheckFails("quantity + 1", ExpressionType.Boolean, scope);

        Assert.Equal(ExpressionDiagnosticCodes.ResultTypeMismatch, diagnostic.Code);
        Assert.Equal(0, diagnostic.Offset);
        Assert.Equal("The expression must be boolean, but it is integer.", diagnostic.Message);
    }

    [Theory]
    [InlineData("i", "decimal")]
    [InlineData("i * 2", "decimal")]
    [InlineData("'draft'", "enum")]
    [InlineData("e", "enum")]
    [InlineData("r", "reference")]
    [InlineData("i > 0", "boolean")]
    public void The_result_fits_the_expected_type_under_the_rules_of_equality(string text, string expected)
    {
        var result = Check(text, Expected(expected));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Theory]
    // An integer widens to a decimal, but a decimal never narrows to an integer.
    [InlineData("d", "integer")]
    [InlineData("i / 2", "integer")]
    [InlineData("t", "enum")]
    [InlineData("e2", "enum")]
    [InlineData("r2", "reference")]
    [InlineData("dt", "date-time")]
    public void A_result_that_does_not_fit_is_a_mismatch(string text, string expected)
    {
        Assert.Equal(ExpressionDiagnosticCodes.ResultTypeMismatch, CheckFails(text, Expected(expected)).Code);
    }

    [Theory]
    [InlineData("e == 'archived'", 5)]
    [InlineData("'Draft' == e", 0)]
    [InlineData("e != 'archived'", 5)]
    [InlineData("e in ('draft', 'archived')", 15)]
    public void A_text_literal_that_is_not_an_enum_value_is_reported(string text, int offset)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownEnumValue, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Contains("enum of 'status'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_literal_result_that_is_not_an_enum_value_is_reported()
    {
        var diagnostic = CheckFails("'archived'", Expected("enum"));

        Assert.Equal(ExpressionDiagnosticCodes.UnknownEnumValue, diagnostic.Code);
        Assert.Equal("'archived' is not a value of enum of 'status' at character 1.", diagnostic.Message);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void Null_tests_are_valid_for_every_field_type(string field)
    {
        foreach (var text in (string[])[$"{field} == null", $"null == {field}", $"{field} != null", $"{field} is null", $"{field} is not null"])
        {
            var result = Check(text, ExpressionType.Boolean);

            Assert.True(result.Succeeded, $"{text}: {result.Diagnostic?.Message}");
        }
    }

    [Theory]
    [InlineData("null + 1", "integer")]
    [InlineData("i - null", "integer")]
    [InlineData("d * null", "decimal")]
    [InlineData("null / i", "decimal")]
    [InlineData("null + null", "null")]
    [InlineData("-null", "null")]
    [InlineData("null < i", "boolean")]
    [InlineData("dt > null", "boolean")]
    [InlineData("null and b", "boolean")]
    [InlineData("b or null", "boolean")]
    [InlineData("not null", "boolean")]
    [InlineData("null == null", "boolean")]
    [InlineData("null is null", "boolean")]
    public void Null_fits_arithmetic_ordering_and_logic(string text, string expected)
    {
        var result = Check(text, ExpressionType.Null);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, result.Type.ToString());
    }

    [Theory]
    [InlineData("text")]
    [InlineData("integer")]
    [InlineData("decimal")]
    [InlineData("boolean")]
    [InlineData("date")]
    [InlineData("date-time")]
    [InlineData("enum")]
    [InlineData("reference")]
    public void Null_fits_any_expected_type(string expected)
    {
        var result = Check("null", Expected(expected));

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(ExpressionType.Null, result.Type);
    }

    [Fact]
    public void Types_are_equal_by_kind_and_source_ignoring_letter_case()
    {
        Assert.Equal(ExpressionType.Enum("status", ["a"]), ExpressionType.Enum("Status", ["b"]));
        Assert.Equal(ExpressionType.Reference("department"), ExpressionType.Reference("DEPARTMENT"));
        Assert.NotEqual(ExpressionType.Enum("status", ["a"]), ExpressionType.Enum("priority", ["a"]));
        Assert.NotEqual(ExpressionType.Reference("department"), ExpressionType.Reference("supplier"));
        Assert.NotEqual(ExpressionType.Date, ExpressionType.DateTime);
        Assert.Equal(
            ExpressionType.Enum("status", []).GetHashCode(),
            ExpressionType.Enum("STATUS", []).GetHashCode());
    }

    [Fact]
    public void The_first_type_error_is_the_only_one_reported()
    {
        var diagnostic = CheckFails("missing + t", ExpressionType.Boolean);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, diagnostic.Code);
        Assert.Equal(0, diagnostic.Offset);
    }

    [Theory]
    [InlineData("IsPositive(i)", "boolean")]
    [InlineData("ispositive(i + 1) and b", "boolean")]
    [InlineData("IsPositive(null)", "boolean")]
    // An integer argument widens to a decimal parameter.
    [InlineData("Twice(i)", "decimal")]
    [InlineData("Twice(d) > 1", "boolean")]
    public void A_rule_call_has_the_rule_result_type(string text, string expected)
    {
        var result = Check(text, ExpressionType.Null, RuleScope());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, result.Type.ToString());
    }

    [Theory]
    [InlineData("Nope(i)", ExpressionDiagnosticCodes.UnknownFunction, "Unknown function or rule 'Nope' at character 1.")]
    [InlineData("IsPositive()", ExpressionDiagnosticCodes.WrongArgumentCount, "Rule 'IsPositive' needs 1 argument, found 0 at character 1.")]
    [InlineData("b and isPositive(i, i)", ExpressionDiagnosticCodes.WrongArgumentCount, "Rule 'IsPositive' needs 1 argument, found 2 at character 7.")]
    [InlineData("IsPositive('a')", ExpressionDiagnosticCodes.TypeMismatch, "Rule 'IsPositive' needs integer for argument 1, found text at character 1.")]
    // A decimal never narrows to an integer parameter.
    [InlineData("b and IsPositive(d)", ExpressionDiagnosticCodes.TypeMismatch, "Rule 'IsPositive' needs integer for argument 1, found decimal at character 7.")]
    public void A_wrong_rule_call_is_reported_at_the_call(string text, string code, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean, RuleScope());

        Assert.Equal((code, message), (diagnostic.Code, diagnostic.Message));
    }

    [Fact]
    public void A_rule_is_unknown_outside_a_scope_that_has_it()
    {
        Assert.Equal(ExpressionDiagnosticCodes.UnknownFunction, CheckFails("IsPositive(i)", ExpressionType.Boolean).Code);
    }

    /// <summary>The fields of <see cref="_scope"/>, with two rules known by their signatures.</summary>
    private static ExpressionScope RuleScope() => new(
        new Dictionary<string, ExpressionType>
        {
            ["t"] = ExpressionType.Text,
            ["i"] = ExpressionType.Integer,
            ["d"] = ExpressionType.Decimal,
            ["b"] = ExpressionType.Boolean,
        },
        [
            new ExpressionRule("IsPositive", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean),
            new ExpressionRule("Twice", [new ExpressionRuleParameter("value", ExpressionType.Decimal)], ExpressionType.Decimal),
        ]);

    private static ExpressionType Expected(string name) => name switch
    {
        "text" => ExpressionType.Text,
        "integer" => ExpressionType.Integer,
        "decimal" => ExpressionType.Decimal,
        "boolean" => ExpressionType.Boolean,
        "date" => ExpressionType.Date,
        "date-time" => ExpressionType.DateTime,
        "enum" => ExpressionType.Enum("status", ["draft", "submitted"]),
        "reference" => ExpressionType.Reference("department"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown type name."),
    };

    private static ExpressionCheckResult Check(string text, ExpressionType expected, ExpressionScope? scope = null)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        return ExpressionTypeChecker.Check(parsed.Expression, scope ?? _scope, expected);
    }

    private static ExpressionDiagnostic CheckFails(string text, ExpressionType expected, ExpressionScope? scope = null)
    {
        var result = Check(text, expected, scope);
        Assert.False(result.Succeeded, $"Expected a diagnostic for '{text}', got type {result.Type}.");
        return result.Diagnostic;
    }
}
