using System.Diagnostics;
using System.Globalization;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

public sealed class ExpressionInterpreterTests
{
    private static readonly Dictionary<string, ExpressionType> _fields = new()
    {
        ["t"] = ExpressionType.Text,
        ["i"] = ExpressionType.Integer,
        ["d"] = ExpressionType.Decimal,
        ["b"] = ExpressionType.Boolean,
        ["c"] = ExpressionType.Boolean,
        ["dt"] = ExpressionType.Date,
        ["ts"] = ExpressionType.DateTime,
        ["e"] = ExpressionType.Enum("status", ["draft", "submitted"]),
        ["r"] = ExpressionType.Reference("department"),
    };

    private static readonly ExpressionScope _scope = new(_fields);

    private static readonly Guid _sales = Guid.Parse("0199a9e5-7c1e-7000-8000-000000000011");
    private static readonly Guid _support = Guid.Parse("0199a9e5-7c1e-7000-8000-000000000012");
    private static readonly Guid _binh = Guid.Parse("0199a9e5-7c1e-7000-8000-000000000021");

    /// <summary>The time a worst-case expression may take to finish or run out of steps.</summary>
    private static readonly TimeSpan _worstCaseTime = TimeSpan.FromSeconds(2);

    /// <summary>A value for every field, so that no result is <c>null</c> unless the expression makes it so.</summary>
    private static readonly Dictionary<string, object?> _setValues = new()
    {
        ["t"] = "a",
        ["i"] = 3L,
        ["d"] = 2.5m,
        ["b"] = true,
        ["c"] = false,
        ["dt"] = new DateOnly(2026, 10, 8),
        ["ts"] = new DateTimeOffset(2026, 10, 8, 2, 30, 0, TimeSpan.Zero),
        ["e"] = "draft",
        ["r"] = Guid.Parse("0199a9e5-7c1e-7000-8000-000000000001"),
    };

    [Theory]
    [InlineData("0.1 + 0.2 == 0.3")]
    [InlineData("0.3 - 0.1 == 0.2")]
    [InlineData("1.1 * 1.1 == 1.21")]
    [InlineData("1 + 0.5 == 1.5")]
    [InlineData("2 == 2.00")]
    public void Decimal_arithmetic_is_exact(string text)
    {
        Assert.Equal(true, Evaluate(text).Value);
    }

    [Theory]
    [InlineData("1 / 3", "0.33333333333333333333")]
    [InlineData("2 / 3", "0.66666666666666666667")]
    [InlineData("-2 / 3", "-0.66666666666666666667")]
    [InlineData("1 / 8", "0.12500000000000000000")]
    [InlineData("0.00000000000000000001 / 2", "0.00000000000000000001")]
    [InlineData("0.00000000000000000001 / 3", "0.00000000000000000000")]
    // A quotient whose whole-number part leaves no room for 20 digits keeps as many as fit.
    [InlineData("10000000000 / 3", "3333333333.3333333333333333333")]
    [InlineData("9223372036854775807 / 7", "1317624576693539401.0000000000")]
    [InlineData("9223372036854775807 / 0.001", "9223372036854775807000.000000")]
    public void Division_rounds_half_away_from_zero_to_20_digits_or_as_many_as_fit(string text, string expected)
    {
        var value = Assert.IsType<decimal>(Evaluate(text).Value);
        Assert.Equal(expected, value.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    // 10^-14 squared needs 28 digits after the point, which fits.
    [InlineData("0.00000000000001", "0.0000000000000000000000000001")]
    [InlineData("1.5", "2.25")]
    public void A_decimal_product_that_fits_is_exact(string d, string expected)
    {
        var value = Assert.IsType<decimal>(Evaluate("d * d", ("d", decimal.Parse(d, CultureInfo.InvariantCulture))).Value);
        Assert.Equal(expected, value.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("9223372036854775807 + 1", 20)]
    [InlineData("i + 1", 2)]
    [InlineData("i * 2", 2)]
    [InlineData("0 - i - 2", 6)]
    public void Integer_overflow_is_a_run_time_error(string text, int offset)
    {
        var error = EvaluateFails(text, ("i", long.MaxValue));

        Assert.Equal(ExpressionRuntimeErrorKind.IntegerOverflow, error.Kind);
        Assert.Equal(offset, error.Offset);
        Assert.EndsWith($"is outside the integer range at character {offset + 1}.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Negating_the_smallest_integer_is_an_overflow()
    {
        var error = EvaluateFails("-i", ("i", long.MinValue));

        Assert.Equal(ExpressionRuntimeErrorKind.IntegerOverflow, error.Kind);
        Assert.Equal(0, error.Offset);
    }

    [Theory]
    // Past the largest decimal.
    [InlineData("d + 1", "79228162514264337593543950335", 2)]
    [InlineData("d * 2", "79228162514264337593543950335", 2)]
    [InlineData("0 - d - 1", "79228162514264337593543950335", 6)]
    // Too many digits: the sum needs 29 significant digits, which decimal would round.
    [InlineData("d + 0.1", "9999999999999999999999999999", 2)]
    // 10^-15 squared needs 30 digits after the point.
    [InlineData("d * d", "0.000000000000001", 2)]
    // A quotient whose whole-number part alone does not fit.
    [InlineData("d / 0.1", "79228162514264337593543950335", 2)]
    public void A_decimal_result_that_cannot_be_held_exactly_is_a_run_time_error(string text, string d, int offset)
    {
        var error = EvaluateFails(text, ("d", decimal.Parse(d, CultureInfo.InvariantCulture)));

        Assert.Equal(ExpressionRuntimeErrorKind.DecimalOverflow, error.Kind);
        Assert.Equal(offset, error.Offset);
        Assert.EndsWith($"cannot be held exactly as a decimal at character {offset + 1}.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 / 0", 2)]
    [InlineData("1.5 / 0.0", 4)]
    [InlineData("d / i", 2)]
    public void Division_by_zero_is_a_run_time_error(string text, int offset)
    {
        var error = EvaluateFails(text, ("d", 1m), ("i", 0L));

        Assert.Equal(ExpressionRuntimeErrorKind.DivisionByZero, error.Kind);
        Assert.Equal(offset, error.Offset);
        Assert.Equal($"Division by zero at character {offset + 1}.", error.Message);
    }

    [Theory]
    [InlineData("date('2026-13-45') == dt", "'2026-13-45' is not a valid date at character 1.")]
    [InlineData("dt == date('2026-02-29')", "'2026-02-29' is not a valid date at character 7.")]
    [InlineData("ts == dateTime('2026-10-08T09:30:00')", "'2026-10-08T09:30:00' is not a valid date-time at character 7.")]
    [InlineData("ts == dateTime('2026-10-08T24:00:00Z')", "'2026-10-08T24:00:00Z' is not a valid date-time at character 7.")]
    [InlineData("ts == dateTime('2026-10-08T09:30:00.1234567Z')", "'2026-10-08T09:30:00.1234567Z' is not a valid date-time at character 7.")]
    [InlineData("ts == dateTime('0001-01-01T00:00:00+01:00')", "'0001-01-01T00:00:00+01:00' is not a valid date-time at character 7.")]
    public void A_date_literal_that_is_not_a_valid_date_is_a_run_time_error(string text, string message)
    {
        // The type checker rejects these literals, so only a tree that skipped it reaches this guard.
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var result = ExpressionInterpreter.Evaluate(parsed.Expression, new ExpressionCheckResult(ExpressionType.Boolean, null), Values([]));
        Assert.False(result.Succeeded, $"Expected a run-time error for '{text}', got {result.Value}.");
        var error = result.Error;

        Assert.Equal(ExpressionRuntimeErrorKind.InvalidDateLiteral, error.Kind);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Date_literals_read_the_record_API_forms()
    {
        Assert.Equal(new DateOnly(2026, 10, 8), Evaluate("date('2026-10-08')").Value);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 8, 2, 30, 0, 500, TimeSpan.Zero),
            Evaluate("dateTime('2026-10-08t09:30:00.5+07:00')").Value);
        Assert.Equal(TimeSpan.Zero, Assert.IsType<DateTimeOffset>(Evaluate("dateTime('2026-10-08T09:30:00-05:00')").Value).Offset);
    }

    [Theory]
    [InlineData("i + 1")]
    [InlineData("1 - i")]
    [InlineData("d * 2")]
    [InlineData("-i")]
    [InlineData("i > 0")]
    [InlineData("dt <= date('2026-10-08')")]
    [InlineData("null < 1")]
    // Null is checked before errors, so these are null rather than errors.
    [InlineData("null / 0")]
    [InlineData("i / 0")]
    [InlineData("9223372036854775807 + i")]
    public void Arithmetic_and_ordering_propagate_null(string text)
    {
        var result = Evaluate(text, ("i", null), ("d", null), ("dt", null));

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("null == null", true)]
    [InlineData("i == null", true)]
    [InlineData("i != null", false)]
    [InlineData("i == 1", false)]
    [InlineData("i != 1", true)]
    [InlineData("t != 'a'", true)]
    [InlineData("i is null", true)]
    [InlineData("i is not null", false)]
    [InlineData("i in (1, 2)", false)]
    [InlineData("not (i in (1))", true)]
    [InlineData("not t in ('a', 'b')", true)]
    public void Equality_and_membership_are_null_safe(string text, bool expected)
    {
        Assert.Equal(expected, Evaluate(text, ("i", null), ("t", null)).Value);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, null, null, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, null, false, null)]
    [InlineData(null, true, null, true)]
    [InlineData(null, false, false, null)]
    [InlineData(null, null, null, null)]
    public void And_and_or_are_three_valued(bool? b, bool? c, bool? and, bool? or)
    {
        Assert.Equal(and, Evaluate("b and c", ("b", b), ("c", c)).Value);
        Assert.Equal(or, Evaluate("b or c", ("b", b), ("c", c)).Value);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(null, null)]
    public void Not_is_three_valued(bool? b, bool? expected)
    {
        Assert.Equal(expected, Evaluate("not b", ("b", b)).Value);
    }

    [Theory]
    [InlineData("false and 1 / 0 > 0", false)]
    [InlineData("true or 1 / 0 > 0", true)]
    [InlineData("i == 0 or 10 / i > 5", true)]
    public void And_and_or_do_not_evaluate_the_right_side_when_the_left_decides(string text, bool expected)
    {
        var result = Evaluate(text, ("i", 0L));

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("i == 3.0", true)]
    [InlineData("i in (1, 3.00)", true)]
    [InlineData("i < d", false)]
    [InlineData("d >= 2.50", true)]
    [InlineData("t == 'A'", false)]
    [InlineData("e == 'draft'", true)]
    [InlineData("e in ('submitted')", false)]
    [InlineData("b != c", true)]
    [InlineData("r == r", true)]
    [InlineData("dt in (date('2026-10-07'), date('2026-10-08'))", true)]
    [InlineData("ts == dateTime('2026-10-08T09:30:00+07:00')", true)]
    [InlineData("ts < dateTime('2026-10-08T02:30:00.000001Z')", true)]
    public void Comparisons_follow_the_value_rules(string text, bool expected)
    {
        Assert.Equal(expected, Evaluate(text).Value);
    }

    [Fact]
    public void Evaluation_stops_when_the_step_budget_runs_out()
    {
        // 2^14 leaves make 32,767 nodes. The division by zero is evaluated last, after the budget has run out.
        var expression = Sum(1 << 14, new BinaryNode(5, BinaryOperator.Divide, new IntegerLiteral(4, 1), new IntegerLiteral(6, 0)));

        var result = ExpressionInterpreter.Evaluate(expression, new ExpressionCheckResult(ExpressionType.Decimal, null), Values([]));

        Assert.False(result.Succeeded);
        Assert.Equal(ExpressionRuntimeErrorKind.StepBudgetExhausted, result.Error.Kind);
        Assert.EndsWith("ran past its budget of 10000 steps at character 2.", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluation_may_use_the_whole_step_budget()
    {
        // 5,000 leaves make 9,999 nodes, and the minus on top makes 10,000.
        var exact = new UnaryNode(0, UnaryOperator.Negate, Sum(5_000, new IntegerLiteral(0, 1)));
        var over = new UnaryNode(0, UnaryOperator.Negate, exact);
        var checkedType = new ExpressionCheckResult(ExpressionType.Integer, null);

        Assert.Equal(-5_000L, ExpressionInterpreter.Evaluate(exact, checkedType, Values([])).Value);
        Assert.Equal(
            ExpressionRuntimeErrorKind.StepBudgetExhausted,
            ExpressionInterpreter.Evaluate(over, checkedType, Values([])).Error?.Kind);
    }

    [Theory]
    [InlineData(3L, true)]
    [InlineData(0L, false)]
    [InlineData(null, null)]
    public void A_rule_call_gives_the_result_of_the_rule_body(long? i, bool? expected)
    {
        var isPositive = Rule("IsPositive", "value > 0", ExpressionType.Boolean, ("value", ExpressionType.Integer));

        var result = EvaluateWithRules("IsPositive(i)", [isPositive], ("i", i));

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void A_rule_body_can_call_another_rule()
    {
        var isPositive = Rule("IsPositive", "value > 0", ExpressionType.Boolean, ("value", ExpressionType.Integer));
        var bothPositive = Rule(
            "BothPositive", "isPositive(a) and IsPositive(b)", ExpressionType.Boolean, [isPositive], ("a", ExpressionType.Integer), ("b", ExpressionType.Integer));

        Assert.Equal(true, EvaluateWithRules("BothPositive(i, 1)", [bothPositive]).Value);
        Assert.Equal(false, EvaluateWithRules("BothPositive(i, -1)", [bothPositive]).Value);
    }

    [Fact]
    public void An_integer_becomes_a_decimal_for_a_decimal_parameter_or_result()
    {
        // The parameter is a decimal, so value / 2 sees 3 as 3.0 and the result is a decimal.
        var half = Rule("Half", "value / 2", ExpressionType.Decimal, ("value", ExpressionType.Decimal));
        var three = Rule("Three", "3", ExpressionType.Decimal);
        var same = Rule("Same", "value", ExpressionType.Decimal, ("value", ExpressionType.Decimal));

        Assert.Equal(1.5m, Assert.IsType<decimal>(EvaluateWithRules("Half(i)", [half]).Value));
        Assert.Equal(3m, Assert.IsType<decimal>(EvaluateWithRules("Three()", [three]).Value));
        Assert.Equal(3m, Assert.IsType<decimal>(EvaluateWithRules("Same(i)", [same]).Value));
    }

    [Fact]
    public void The_steps_of_a_rule_body_count_against_the_caller_budget()
    {
        // 5,000 leaves make 9,999 nodes in the body, and the call makes 10,000.
        var body = Sum(5_000, new IntegerLiteral(0, 1));
        var big = new ExpressionRule("Big", [], ExpressionType.Integer)
        {
            Body = body,
            BodyCheck = new ExpressionCheckResult(ExpressionType.Integer, null),
        };

        Assert.Equal(5_000L, EvaluateWithRules("Big()", [big]).Value);
        var over = EvaluateWithRules("-Big()", [big]);
        Assert.Equal(ExpressionRuntimeErrorKind.StepBudgetExhausted, over.Error?.Kind);
    }

    [Fact]
    public void A_validation_that_fans_out_through_rule_calls_stops_within_the_time_limit()
    {
        // F0 adds one, and each level above calls the one below 4 times: 8 nested levels and 4^7 leaf calls.
        var rule = Rule("F0", "x + 1", ExpressionType.Integer, ("x", ExpressionType.Integer));
        for (var k = 1; k <= 7; k++)
        {
            var below = $"F{k - 1}(x)";
            rule = Rule($"F{k}", $"{below} + {below} + {below} + {below}", ExpressionType.Integer, [rule], ("x", ExpressionType.Integer));
        }

        var stopwatch = Stopwatch.StartNew();
        var result = EvaluateWithRules("F7(i) > 0", [rule]);
        stopwatch.Stop();

        Assert.Equal(ExpressionRuntimeErrorKind.StepBudgetExhausted, result.Error?.Kind);
        Assert.True(stopwatch.Elapsed < _worstCaseTime, $"The evaluation took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Text_doubled_through_rule_calls_stops_within_the_time_limit()
    {
        // D1 doubles its text, and each level above applies the one below twice, so D8 would double it 2^7 times.
        var rule = Rule("D1", "concat(s, s)", ExpressionType.Text, ("s", ExpressionType.Text));
        for (var k = 2; k <= 8; k++)
        {
            rule = Rule($"D{k}", $"D{k - 1}(D{k - 1}(s))", ExpressionType.Text, [rule], ("s", ExpressionType.Text));
        }

        var stopwatch = Stopwatch.StartNew();
        var result = EvaluateWithRules("length(D8(t)) > 0", [rule]);
        stopwatch.Stop();

        Assert.Equal(ExpressionRuntimeErrorKind.StepBudgetExhausted, result.Error?.Kind);
        Assert.True(stopwatch.Elapsed < _worstCaseTime, $"The evaluation took {stopwatch.Elapsed}.");
    }

    [Theory]
    // The call and its 10 names are 11 steps. 10 × 998,900 characters add 9,989 more, which makes exactly 10,000.
    [InlineData(998_900, true)]
    [InlineData(999_000, false)]
    public void A_concat_costs_one_step_for_every_full_thousand_characters_it_builds(int length, bool succeeds)
    {
        var result = Evaluate("concat(t, t, t, t, t, t, t, t, t, t)", ("t", new string('a', length)));

        if (succeeds)
        {
            Assert.True(result.Succeeded, result.Error?.Message);
            Assert.Equal(10 * length, Assert.IsType<string>(result.Value).Length);
        }
        else
        {
            Assert.Equal(ExpressionRuntimeErrorKind.StepBudgetExhausted, result.Error?.Kind);
        }
    }

    [Fact]
    public void A_rule_known_only_by_its_signature_is_a_bug_to_evaluate()
    {
        var signature = new ExpressionRule("IsPositive", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean);

        Assert.Throws<InvalidOperationException>(() => EvaluateWithRules("IsPositive(i)", [signature]));
    }

    public static TheoryData<string, bool> Corpus
    {
        get
        {
            string[] expressions =
            [
                "1", "1.5", "'a'", "true", "null",
                "t", "i", "d", "b", "dt", "ts", "e", "r",
                "date('2026-10-08')", "dateTime('2026-10-08T09:30:00+07:00')",
                "i + i", "i - 1", "i * 2", "i + d", "d - i", "1.5 * i", "i / 2", "d / d", "-i", "-d",
                "null + null", "null * 1", "i + null",
                "i < d", "dt >= date('2026-01-01')", "ts > dateTime('2026-10-08T09:30:00Z')",
                "t == 'a'", "e != 'draft'", "r == r", "i == d",
                "b and c", "b or c", "not b",
                "i is null", "d is not null",
                "i in (1, 2.5)", "e in ('draft', 'submitted')", "dt in (date('2026-10-08'))",
                // A coalesce or if that mixes integer and decimal gives a decimal, even when it picks the integer.
                "coalesce(d, 1)", "coalesce(i, d)", "coalesce(null, 1, 2.5)",
                "if(b, 1, 2.5)", "if(c, 2.5, 1)", "if(b, i, d)",
                "-coalesce(d, 1)", "floor(if(c, d, 1))",
            ];

            var data = new TheoryData<string, bool>();
            foreach (var expression in expressions)
            {
                data.Add(expression, false);
                data.Add(expression, true);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void The_run_time_type_of_a_result_matches_its_checked_type(string text, bool fieldsAreNull)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var checkedType = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Null);
        Assert.True(checkedType.Succeeded, checkedType.Diagnostic?.Message);

        var values = fieldsAreNull ? _fields.Keys.ToDictionary(name => name, _ => (object?)null) : _setValues;
        var result = ExpressionInterpreter.Evaluate(parsed.Expression, checkedType, new ExpressionValues(values));

        Assert.True(result.Succeeded, result.Error?.Message);
        if (checkedType.Type.Kind == ExpressionTypeKind.Null)
        {
            Assert.Null(result.Value);
        }
        else if (result.Value is not null)
        {
            Assert.Equal(ClrType(checkedType.Type.Kind), result.Value.GetType());
        }
    }

    [Theory]
    // count, sum, min, max, any and all on an empty list follow the reference table.
    [InlineData("count(lines)", 0L)]
    [InlineData("count(lines, ok)", 0L)]
    [InlineData("sum(lines, qty)", 0L)]
    [InlineData("min(lines, qty)", null)]
    [InlineData("max(lines, amount)", null)]
    [InlineData("any(lines, ok)", false)]
    [InlineData("all(lines, ok)", true)]
    public void An_aggregate_over_an_empty_list_gives_its_empty_value(string text, object? expected)
    {
        var result = EvaluateOver(text, Rows());

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void A_decimal_sum_of_an_empty_list_is_a_decimal_zero()
    {
        Assert.Equal(0m, Assert.IsType<decimal>(EvaluateOver("sum(lines, amount)", Rows()).Value));
    }

    [Theory]
    // Null items are skipped, and a null condition counts as false.
    [InlineData("count(lines)", 4L)]
    [InlineData("count(lines, ok)", 1L)]
    [InlineData("count(lines, not ok)", 1L)]
    [InlineData("sum(lines, qty)", 6L)]
    [InlineData("min(lines, qty)", 1L)]
    [InlineData("max(lines, qty)", 3L)]
    [InlineData("any(lines, ok)", true)]
    [InlineData("any(lines, qty > 5)", false)]
    [InlineData("all(lines, qty > 0)", false)]
    [InlineData("all(lines, qty is null or qty > 0)", true)]
    public void An_aggregate_skips_null_items_and_null_conditions(string text, object? expected)
    {
        var lines = Rows((2L, 1.5m, true), (null, null, null), (3L, 0.25m, false), (1L, null, null));

        var result = EvaluateOver(text, lines);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void A_null_condition_makes_all_false()
    {
        Assert.Equal(false, EvaluateOver("all(lines, ok)", Rows((1L, null, true), (2L, null, null))).Value);
        Assert.Equal(true, EvaluateOver("all(lines, ok)", Rows((1L, null, true), (2L, null, true))).Value);
    }

    [Fact]
    public void Sum_min_and_max_of_decimals_are_exact_and_keep_the_item_type()
    {
        var lines = Rows((2L, 0.1m, null), (null, 0.2m, null), (3L, null, null));

        Assert.Equal(0.3m, Assert.IsType<decimal>(EvaluateOver("sum(lines, amount)", lines).Value));
        Assert.Equal(0.1m, Assert.IsType<decimal>(EvaluateOver("min(lines, amount)", lines).Value));
        Assert.Equal(5L, Assert.IsType<long>(EvaluateOver("sum(lines, qty)", lines).Value));
        // An integer item widens to a decimal when the item expression is decimal.
        Assert.Equal(2.3m, Assert.IsType<decimal>(EvaluateOver("sum(lines, coalesce(amount, 2))", lines).Value));
    }

    [Fact]
    public void An_all_null_list_sums_to_zero_and_has_no_min_or_max()
    {
        var lines = Rows((null, null, null), (null, null, null));

        Assert.Equal(0L, EvaluateOver("sum(lines, qty)", lines).Value);
        Assert.Equal(0m, EvaluateOver("sum(lines, amount)", lines).Value);
        Assert.Null(EvaluateOver("min(lines, qty)", lines).Value);
        Assert.Null(EvaluateOver("max(lines, amount)", lines).Value);
    }

    [Fact]
    public void Min_and_max_order_dates()
    {
        var lines = (IReadOnlyList<ExpressionValues>)
        [
            Row(null, null, null, new DateOnly(2026, 3, 1)),
            Row(null, null, null, null),
            Row(null, null, null, new DateOnly(2025, 12, 31)),
        ];

        Assert.Equal(new DateOnly(2025, 12, 31), EvaluateOver("min(lines, due)", lines).Value);
        Assert.Equal(new DateOnly(2026, 3, 1), EvaluateOver("max(lines, due)", lines).Value);
    }

    [Fact]
    public void An_integer_sum_past_the_range_is_an_overflow_at_the_call()
    {
        var result = EvaluateOver("1 + sum(lines, qty)", Rows((long.MaxValue, null, null), (1L, null, null)));

        Assert.False(result.Succeeded);
        Assert.Equal(ExpressionRuntimeErrorKind.IntegerOverflow, result.Error.Kind);
        Assert.Equal(4, result.Error.Offset);
    }

    [Fact]
    public void Every_row_is_evaluated_so_a_later_error_is_reported()
    {
        var result = EvaluateOver("any(lines, qty / qty > 0)", Rows((1L, null, null), (0L, null, null)));

        Assert.Equal(ExpressionRuntimeErrorKind.DivisionByZero, result.Error?.Kind);
    }

    [Fact]
    public void Each_row_of_an_aggregate_counts_against_the_step_budget()
    {
        // The call is one step and each row's item is one more: 1 + 9,999 is the whole budget.
        var within = EvaluateOver("sum(lines, qty)", Rows([.. Enumerable.Repeat<(long?, decimal?, bool?)>((1L, null, null), 9_999)]));
        var over = EvaluateOver("sum(lines, qty)", Rows([.. Enumerable.Repeat<(long?, decimal?, bool?)>((1L, null, null), 10_000)]));

        Assert.Equal(9_999L, within.Value);
        Assert.Equal(ExpressionRuntimeErrorKind.StepBudgetExhausted, over.Error?.Kind);
    }

    [Fact]
    public void A_path_reads_the_field_of_the_record_each_reference_names()
    {
        Assert.Equal("Binh", EvaluatePath("r.manager.name", _sales).Value);
        Assert.Equal("Sales", EvaluatePath("r.name", _sales).Value);
    }

    [Fact]
    public void A_null_reference_along_a_path_gives_null()
    {
        Assert.Null(EvaluatePath("r.manager.name", null).Value);
        Assert.Null(EvaluatePath("r.manager.name", _support).Value);
        Assert.Equal(true, EvaluatePath("r.manager is null", _support).Value);
    }

    [Fact]
    public void A_reference_that_names_no_record_gives_null()
    {
        Assert.Null(EvaluatePath("r.name", Guid.Parse("0199a9e5-7c1e-7000-8000-0000000000ff")).Value);
    }

    [Fact]
    public void A_path_that_ends_at_a_reference_gives_its_id()
    {
        Assert.Equal(_binh, EvaluatePath("r.manager", _sales).Value);
        Assert.Equal(true, EvaluatePath("r.manager.department == r", _sales).Value);
        Assert.Equal(false, EvaluatePath("r.manager.department == r", _support).Value);
    }

    [Fact]
    public void A_path_without_a_record_resolver_is_a_bug()
    {
        var parsed = ExpressionParser.Parse("r.name");
        var check = ExpressionTypeChecker.Check(parsed.Expression!, PathScope(), ExpressionType.Null);

        Assert.Throws<InvalidOperationException>(() =>
            ExpressionInterpreter.Evaluate(parsed.Expression!, check, Values([("r", _sales)])));
    }

    [Fact]
    public void A_value_of_an_unexpected_type_is_a_bug()
    {
        Assert.Throws<InvalidOperationException>(() => Evaluate("i + 1", ("i", 1)));
    }

    private static Type ClrType(ExpressionTypeKind kind) => kind switch
    {
        ExpressionTypeKind.Text or ExpressionTypeKind.Enum => typeof(string),
        ExpressionTypeKind.Integer => typeof(long),
        ExpressionTypeKind.Decimal => typeof(decimal),
        ExpressionTypeKind.Boolean => typeof(bool),
        ExpressionTypeKind.Date => typeof(DateOnly),
        ExpressionTypeKind.DateTime => typeof(DateTimeOffset),
        ExpressionTypeKind.Reference => typeof(Guid),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>A balanced sum of <paramref name="leaves"/> ones, with <paramref name="last"/> as the last leaf.</summary>
    private static ExpressionNode Sum(int leaves, ExpressionNode last)
    {
        return Build(0, leaves);

        ExpressionNode Build(int start, int count)
        {
            if (count == 1)
            {
                return start == leaves - 1 ? last : new IntegerLiteral(0, 1);
            }

            var half = count / 2;
            return new BinaryNode(1, BinaryOperator.Add, Build(start, half), Build(start + half, count - half));
        }
    }

    /// <summary>
    /// The field <c>r</c> of <see cref="_fields"/> names a department with a name and a manager,
    /// and a manager is an employee with a name and a department.
    /// </summary>
    private static ExpressionScope PathScope() => new(
        _fields,
        referenceFields: (entity, field) => (entity.ToLowerInvariant(), field.ToLowerInvariant()) switch
        {
            ("department", "name") => ExpressionType.Text,
            ("department", "manager") => ExpressionType.Reference("Employee"),
            ("employee", "name") => ExpressionType.Text,
            ("employee", "department") => ExpressionType.Reference("department"),
            _ => null,
        });

    /// <summary>
    /// Evaluates <paramref name="text"/> with <c>r</c> set to <paramref name="department"/>, reading
    /// the stored departments and employees in memory.
    /// </summary>
    private static ExpressionEvaluationResult EvaluatePath(string text, Guid? department)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var checkedType = ExpressionTypeChecker.Check(parsed.Expression, PathScope(), ExpressionType.Null);
        Assert.True(checkedType.Succeeded, checkedType.Diagnostic?.Message);
        return ExpressionInterpreter.Evaluate(parsed.Expression, checkedType, Values([("r", department)]), (entity, id) =>
            (entity.ToLowerInvariant(), id) switch
            {
                ("department", var found) when found == _sales => new ExpressionValues(
                    new Dictionary<string, object?> { ["name"] = "Sales", ["manager"] = _binh }),
                ("department", var found) when found == _support => new ExpressionValues(
                    new Dictionary<string, object?> { ["name"] = "Support", ["manager"] = null }),
                ("employee", var found) when found == _binh => new ExpressionValues(
                    new Dictionary<string, object?> { ["name"] = "Binh", ["department"] = _sales }),
                _ => null,
            });
    }

    /// <summary>The values in <see cref="_setValues"/>, with <paramref name="overrides"/> replacing some.</summary>
    private static ExpressionValues Values((string Name, object? Value)[] overrides)
    {
        var values = new Dictionary<string, object?>(_setValues);
        foreach (var (name, value) in overrides)
        {
            values[name] = value;
        }

        return new ExpressionValues(values);
    }

    private static ExpressionEvaluationResult Evaluate(string text, params (string Name, object? Value)[] overrides)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var checkedType = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Null);
        Assert.True(checkedType.Succeeded, checkedType.Diagnostic?.Message);
        return ExpressionInterpreter.Evaluate(parsed.Expression, checkedType, Values(overrides));
    }

    /// <summary>A rule whose body is parsed and checked over its parameters and <paramref name="rules"/>.</summary>
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

    private static ExpressionRule Rule(string name, string body, ExpressionType result, params (string Name, ExpressionType Type)[] parameters) =>
        Rule(name, body, result, [], parameters);

    private static ExpressionEvaluationResult EvaluateWithRules(
        string text, IEnumerable<ExpressionRule> rules, params (string Name, object? Value)[] overrides)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var checkedType = ExpressionTypeChecker.Check(parsed.Expression, new ExpressionScope(_fields, rules), ExpressionType.Null);
        Assert.True(checkedType.Succeeded, checkedType.Diagnostic?.Message);
        return ExpressionInterpreter.Evaluate(parsed.Expression, checkedType, Values(overrides));
    }

    /// <summary>Evaluates <paramref name="text"/> over a record whose collection <c>lines</c> holds <paramref name="lines"/>.</summary>
    private static ExpressionEvaluationResult EvaluateOver(string text, IReadOnlyList<ExpressionValues> lines)
    {
        var items = new ExpressionScope(new Dictionary<string, ExpressionType>
        {
            ["qty"] = ExpressionType.Integer,
            ["amount"] = ExpressionType.Decimal,
            ["ok"] = ExpressionType.Boolean,
            ["due"] = ExpressionType.Date,
        });
        var scope = new ExpressionScope(_fields, collections: [new ExpressionCollection("lines", "Line", items)]);
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var checkedType = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Null);
        Assert.True(checkedType.Succeeded, checkedType.Diagnostic?.Message);
        return ExpressionInterpreter.Evaluate(parsed.Expression, checkedType, Values([("lines", lines)]));
    }

    private static IReadOnlyList<ExpressionValues> Rows(params (long? Qty, decimal? Amount, bool? Ok)[] rows) =>
        [.. rows.Select(row => Row(row.Qty, row.Amount, row.Ok, null))];

    private static ExpressionValues Row(long? qty, decimal? amount, bool? ok, DateOnly? due) =>
        new(new Dictionary<string, object?> { ["qty"] = qty, ["amount"] = amount, ["ok"] = ok, ["due"] = due });

    private static ExpressionRuntimeError EvaluateFails(string text, params (string Name, object? Value)[] overrides)
    {
        var result = Evaluate(text, overrides);
        Assert.False(result.Succeeded, $"Expected a run-time error for '{text}', got {result.Value}.");
        return result.Error;
    }
}
