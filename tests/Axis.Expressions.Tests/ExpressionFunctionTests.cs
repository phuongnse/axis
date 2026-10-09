using System.Globalization;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

public sealed class ExpressionFunctionTests
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
        ["e2"] = ExpressionType.Enum("priority", ["low"]),
    };

    private static readonly ExpressionScope _scope = new(_fields);

    [Theory]
    // Text.
    [InlineData("length('abc')", "integer 3")]
    [InlineData("length('')", "integer 0")]
    // A surrogate pair is one code point.
    [InlineData("length('😀a')", "integer 2")]
    [InlineData("contains('abc', 'b')", "boolean true")]
    [InlineData("contains('abc', 'B')", "boolean false")]
    [InlineData("startsWith('abc', 'ab')", "boolean true")]
    [InlineData("startsWith('abc', 'AB')", "boolean false")]
    [InlineData("endsWith('abc', 'bc')", "boolean true")]
    [InlineData("endsWith('abc', 'b')", "boolean false")]
    [InlineData("concat('a', 'b', 'c')", "text 'abc'")]
    [InlineData("concat('a')", "text 'a'")]
    [InlineData("lower('AbC')", "text 'abc'")]
    [InlineData("upper('aBc')", "text 'ABC'")]
    [InlineData("trim('  a b  ')", "text 'a b'")]
    // Math: abs, floor and ceiling keep the type, round gives a decimal.
    [InlineData("abs(-3)", "integer 3")]
    [InlineData("abs(-2.5)", "decimal 2.5")]
    [InlineData("abs(4)", "integer 4")]
    [InlineData("round(2.345, 2)", "decimal 2.35")]
    [InlineData("round(2.5, 0)", "decimal 3")]
    [InlineData("round(-2.5, 0)", "decimal -3")]
    [InlineData("round(1, 2)", "decimal 1")]
    [InlineData("round(1.5, 28)", "decimal 1.5")]
    [InlineData("floor(2.7)", "decimal 2")]
    [InlineData("floor(-2.5)", "decimal -3")]
    [InlineData("floor(3)", "integer 3")]
    [InlineData("ceiling(2.1)", "decimal 3")]
    [InlineData("ceiling(-2.5)", "decimal -2")]
    [InlineData("ceiling(3)", "integer 3")]
    // Dates.
    [InlineData("year(date('2026-10-08'))", "integer 2026")]
    [InlineData("month(date('2026-10-08'))", "integer 10")]
    [InlineData("day(date('2026-10-08'))", "integer 8")]
    [InlineData("addDays(date('2026-10-08'), 30)", "date 2026-11-07")]
    [InlineData("addDays(date('2026-03-01'), -1)", "date 2026-02-28")]
    [InlineData("addDays(date('9999-12-30'), 1)", "date 9999-12-31")]
    [InlineData("daysBetween(date('2026-10-01'), date('2026-10-08'))", "integer 7")]
    [InlineData("daysBetween(date('2026-10-08'), date('2026-10-01'))", "integer -7")]
    // coalesce and if.
    [InlineData("coalesce(null, 'a')", "text 'a'")]
    [InlineData("coalesce(t, 'b', 'c')", "text 'b'")]
    [InlineData("coalesce(i, 5)", "integer 5")]
    [InlineData("coalesce(d, 1)", "decimal 1")]
    [InlineData("coalesce(i, d, 2)", "decimal 2")]
    [InlineData("if(true, 1, 2)", "integer 1")]
    [InlineData("if(false, 1, 2)", "integer 2")]
    [InlineData("if(b, 1, 2)", "integer 2")]
    [InlineData("if(true, 1, 2.5)", "decimal 1")]
    [InlineData("if(1 > 0, 'yes', 'no')", "text 'yes'")]
    // Names ignore letter case.
    [InlineData("LENGTH('ab')", "integer 2")]
    [InlineData("Coalesce(null, 1)", "integer 1")]
    public void Functions_return_the_documented_results(string text, string expected)
    {
        Assert.Equal(expected, Describe(Evaluate(text).Value));
    }

    [Theory]
    [InlineData("length(t)")]
    [InlineData("contains(t, 'a')")]
    [InlineData("startsWith('a', t)")]
    [InlineData("endsWith(t, t)")]
    [InlineData("lower(t)")]
    [InlineData("upper(null)")]
    [InlineData("trim(t)")]
    [InlineData("abs(i)")]
    [InlineData("abs(null)")]
    [InlineData("round(d, 2)")]
    [InlineData("round(1.5, i)")]
    [InlineData("floor(d)")]
    [InlineData("ceiling(i)")]
    [InlineData("year(dt)")]
    [InlineData("month(dt)")]
    [InlineData("day(dt)")]
    [InlineData("addDays(dt, 1)")]
    [InlineData("addDays(date('2026-10-08'), i)")]
    [InlineData("daysBetween(dt, date('2026-10-08'))")]
    [InlineData("coalesce(t, null)")]
    [InlineData("coalesce(null, null)")]
    [InlineData("if(true, i, 1)")]
    [InlineData("if(true, null, 1)")]
    // Null is checked before errors, so these are null rather than errors.
    [InlineData("round(d, 29)")]
    [InlineData("addDays(dt, 9223372036854775807)")]
    public void Functions_return_null_for_a_null_argument(string text)
    {
        var result = Evaluate(text);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("concat('a', null, 'c')", "ac")]
    [InlineData("concat(t, 'b')", "b")]
    [InlineData("concat(t)", "")]
    [InlineData("concat(null, null)", "")]
    public void Concat_treats_null_as_empty_text(string text, string expected)
    {
        Assert.Equal(expected, Evaluate(text).Value);
    }

    [Theory]
    [InlineData("if(true, 1, 1 / 0)", "decimal 1")]
    [InlineData("if(false, 1 / 0, 2)", "decimal 2")]
    [InlineData("if(b, 1 / 0, 2)", "decimal 2")]
    [InlineData("if(i == 0, 0, 10 / i)", "decimal 0")]
    [InlineData("coalesce(1, 1 / 0)", "decimal 1")]
    [InlineData("coalesce(d, 2, 1 / 0)", "decimal 2")]
    public void If_and_coalesce_do_not_evaluate_the_arguments_they_do_not_pick(string text, string expected)
    {
        var result = Evaluate(text, ("i", 0L));

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected, Describe(result.Value));
    }

    [Theory]
    [InlineData("addDays(dt, 1)", "9999-12-31", 0)]
    [InlineData("addDays(dt, -1)", "0001-01-01", 0)]
    [InlineData("daysBetween(dt, addDays(dt, 3000000))", "2026-10-08", 16)]
    [InlineData("addDays(dt, 9223372036854775807)", "2026-10-08", 0)]
    [InlineData("addDays(dt, -9223372036854775807 - 1)", "2026-10-08", 0)]
    public void A_date_outside_0001_to_9999_is_a_run_time_error(string text, string date, int offset)
    {
        var error = EvaluateFails(text, ("dt", DateOnly.Parse(date, CultureInfo.InvariantCulture)));

        Assert.Equal(ExpressionRuntimeErrorKind.DateOutOfRange, error.Kind);
        Assert.Equal(offset, error.Offset);
        Assert.Equal($"The result of 'addDays' is outside 0001-01-01 to 9999-12-31 at character {offset + 1}.", error.Message);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(-1)]
    public void A_round_digit_count_outside_0_to_28_is_a_run_time_error(long digits)
    {
        var error = EvaluateFails("round(d, i)", ("d", 1.5m), ("i", digits));

        Assert.Equal(ExpressionRuntimeErrorKind.ArgumentOutOfRange, error.Kind);
        Assert.Equal(0, error.Offset);
        Assert.Equal($"The digit count of 'round' must be from 0 to 28, found {digits} at character 1.", error.Message);
    }

    [Fact]
    public void The_absolute_value_of_the_smallest_integer_is_an_overflow()
    {
        var error = EvaluateFails("abs(i)", ("i", long.MinValue));

        Assert.Equal(ExpressionRuntimeErrorKind.IntegerOverflow, error.Kind);
        Assert.Equal("The result of 'abs' is outside the integer range at character 1.", error.Message);
    }

    [Theory]
    [InlineData("length(t)", "integer")]
    [InlineData("contains(t, null)", "boolean")]
    [InlineData("concat('a', t, null)", "text")]
    [InlineData("trim(null)", "text")]
    [InlineData("abs(i)", "integer")]
    [InlineData("abs(d)", "decimal")]
    [InlineData("abs(null)", "null")]
    [InlineData("floor(d)", "decimal")]
    [InlineData("ceiling(i)", "integer")]
    [InlineData("round(i, 2)", "decimal")]
    [InlineData("round(null, null)", "decimal")]
    [InlineData("year(dt)", "integer")]
    [InlineData("addDays(dt, 1)", "date")]
    [InlineData("daysBetween(dt, null)", "integer")]
    [InlineData("coalesce(i, 1)", "integer")]
    [InlineData("coalesce(i, d)", "decimal")]
    [InlineData("coalesce(null, 1, 2.5)", "decimal")]
    [InlineData("coalesce(null, null)", "null")]
    [InlineData("coalesce(e, 'draft')", "enum of 'status'")]
    [InlineData("coalesce('draft', e)", "enum of 'status'")]
    [InlineData("coalesce(t, 'x')", "text")]
    [InlineData("if(b, 1, 2)", "integer")]
    [InlineData("if(b, 1, 2.5)", "decimal")]
    [InlineData("if(null, 'a', null)", "text")]
    [InlineData("if(b, dt, dt)", "date")]
    public void Calls_have_the_documented_type(string text, string expected)
    {
        var result = Check(text);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, result.Type.ToString());
    }

    [Theory]
    [InlineData("foo(1)", 0, "Unknown function or rule 'foo' at character 1.")]
    [InlineData("i + bar()", 4, "Unknown function or rule 'bar' at character 5.")]
    // The name is looked up before the arguments are checked.
    [InlineData("foo(missing)", 0, "Unknown function or rule 'foo' at character 1.")]
    public void An_unknown_function_is_reported_at_the_call(string text, int offset, string message)
    {
        var diagnostic = CheckFails(text);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownFunction, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("round(1.5)", 0, "Function 'round' needs 2 arguments, found 1 at character 1.")]
    [InlineData("length('a', 'b')", 0, "Function 'length' needs 1 argument, found 2 at character 1.")]
    [InlineData("date('a', 'b')", 0, "Function 'date' needs 1 argument, found 2 at character 1.")]
    [InlineData("i + if(b, 1)", 4, "Function 'if' needs 3 arguments, found 2 at character 5.")]
    [InlineData("concat()", 0, "Function 'concat' needs at least 1 argument, found 0 at character 1.")]
    [InlineData("coalesce(i)", 0, "Function 'coalesce' needs at least 2 arguments, found 1 at character 1.")]
    // The count is checked before the arguments.
    [InlineData("round(missing)", 0, "Function 'round' needs 2 arguments, found 1 at character 1.")]
    public void A_wrong_argument_count_names_the_expected_count(string text, int offset, string message)
    {
        var diagnostic = CheckFails(text);

        Assert.Equal(ExpressionDiagnosticCodes.WrongArgumentCount, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("length(1)", 0, "Function 'length' needs text for argument 1, found integer at character 1.")]
    [InlineData("year(ts)", 0, "Function 'year' needs date for argument 1, found date-time at character 1.")]
    [InlineData("i + length(e)", 4, "Function 'length' needs text for argument 1, found enum of 'status' at character 5.")]
    [InlineData("contains(t, 1)", 0, "Function 'contains' needs text for argument 2, found integer at character 1.")]
    [InlineData("concat('a', b)", 0, "Function 'concat' needs text for argument 2, found boolean at character 1.")]
    [InlineData("abs(t)", 0, "Function 'abs' needs a number for argument 1, found text at character 1.")]
    [InlineData("round(d, 1.5)", 0, "Function 'round' needs integer for argument 2, found decimal at character 1.")]
    [InlineData("addDays(dt, d)", 0, "Function 'addDays' needs integer for argument 2, found decimal at character 1.")]
    [InlineData("daysBetween(dt, ts)", 0, "Function 'daysBetween' needs date for argument 2, found date-time at character 1.")]
    [InlineData("if(i, 1, 2)", 0, "Function 'if' needs boolean for argument 1, found integer at character 1.")]
    [InlineData("coalesce(i, t)", 0, "Function 'coalesce' cannot combine integer and text at character 1.")]
    [InlineData("coalesce(t, e)", 0, "Function 'coalesce' cannot combine text and enum of 'status' at character 1.")]
    [InlineData("coalesce('draft', t, e)", 0, "Function 'coalesce' cannot combine text and enum of 'status' at character 1.")]
    [InlineData("if(b, e, e2)", 0, "Function 'if' cannot combine enum of 'status' and enum of 'priority' at character 1.")]
    [InlineData("if(b, dt, ts)", 0, "Function 'if' cannot combine date and date-time at character 1.")]
    public void A_wrong_argument_type_is_reported_at_the_call(string text, int offset, string message)
    {
        var diagnostic = CheckFails(text);

        Assert.Equal(ExpressionDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("coalesce(e, 'archived')", 12)]
    [InlineData("coalesce('draft', 'archived', e)", 18)]
    public void A_text_literal_combined_with_an_enum_must_be_one_of_its_values(string text, int offset)
    {
        var diagnostic = CheckFails(text);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownEnumValue, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
    }

    /// <summary>A value with its expression type, such as <c>integer 3</c>, so that 3 and 3.0 differ.</summary>
    private static string Describe(object? value) => value switch
    {
        null => "null",
        long integer => $"integer {integer.ToString(CultureInfo.InvariantCulture)}",
        decimal number => $"decimal {number.ToString(CultureInfo.InvariantCulture)}",
        string text => $"text '{text}'",
        bool boolean => boolean ? "boolean true" : "boolean false",
        DateOnly date => $"date {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unexpected value type."),
    };

    private static ExpressionCheckResult Check(string text)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        return ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Null);
    }

    private static ExpressionDiagnostic CheckFails(string text)
    {
        var result = Check(text);
        Assert.False(result.Succeeded, $"Expected a diagnostic for '{text}', got type {result.Type}.");
        return result.Diagnostic;
    }

    /// <summary>Evaluates with every field empty, except the ones in <paramref name="overrides"/>.</summary>
    private static ExpressionEvaluationResult Evaluate(string text, params (string Name, object? Value)[] overrides)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var checkedType = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Null);
        Assert.True(checkedType.Succeeded, checkedType.Diagnostic?.Message);

        var values = _fields.Keys.ToDictionary(name => name, _ => (object?)null);
        foreach (var (name, value) in overrides)
        {
            values[name] = value;
        }

        return ExpressionInterpreter.Evaluate(parsed.Expression, checkedType, new ExpressionValues(values));
    }

    private static ExpressionRuntimeError EvaluateFails(string text, params (string Name, object? Value)[] overrides)
    {
        var result = Evaluate(text, overrides);
        Assert.False(result.Succeeded, $"Expected a run-time error for '{text}', got {result.Value}.");
        return result.Error;
    }
}
