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
        ["pe"] = ExpressionType.EnumParameter("statusFilter", ["draft"]),
        ["pe2"] = ExpressionType.EnumParameter("other", ["draft", "archived"]),
        ["r"] = ExpressionType.Reference("department"),
        ["r2"] = ExpressionType.Reference("supplier"),
        ["f"] = ExpressionType.File,
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
    [InlineData("date('2026-02-28')", "date")]
    [InlineData("dateTime('2026-10-08T09:30:00+07:00')", "date-time")]
    [InlineData("now()", "date-time")]
    [InlineData("NOW()", "date-time")]
    [InlineData("today()", "date")]
    [InlineData("TODAY()", "date")]
    [InlineData("toDate(ts)", "date")]
    [InlineData("toDate(now())", "date")]
    [InlineData("toDate(null)", "date")]
    [InlineData("toDate(dateTime('2026-10-08T09:30:00Z')) == today()", "boolean")]
    [InlineData("daysBetween(toDate(ts), today())", "integer")]
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
    [InlineData("now() > dateTime('2020-01-01T00:00:00Z')", "boolean")]
    [InlineData("ts <= now()", "boolean")]
    // Equality on two values of the same type, with integers widening to decimals.
    [InlineData("t == 'a'", "boolean")]
    [InlineData("i == d", "boolean")]
    [InlineData("b != true", "boolean")]
    [InlineData("dt == dt", "boolean")]
    [InlineData("ts != ts", "boolean")]
    [InlineData("e == e", "boolean")]
    [InlineData("e == 'draft'", "boolean")]
    [InlineData("'submitted' != e", "boolean")]
    // An enum parameter compares with an enum field whose values hold all of its own.
    [InlineData("e == pe", "boolean")]
    [InlineData("pe != e", "boolean")]
    [InlineData("pe == 'draft'", "boolean")]
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
    [InlineData("e == pe2", 2)]
    [InlineData("pe2 == e", 4)]
    [InlineData("pe == e2", 3)]
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
    [InlineData("now(1)", "Function 'now' needs 0 arguments, found 1 at character 1.")]
    [InlineData("today(1)", "Function 'today' needs 0 arguments, found 1 at character 1.")]
    [InlineData("toDate()", "Function 'toDate' needs 1 argument, found 0 at character 1.")]
    [InlineData("toDate(ts, ts)", "Function 'toDate' needs 1 argument, found 2 at character 1.")]
    public void A_date_literal_with_the_wrong_argument_count_is_reported(string text, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null);

        Assert.Equal(ExpressionDiagnosticCodes.WrongArgumentCount, diagnostic.Code);
        Assert.Equal(0, diagnostic.Offset);
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("now()", 0)]
    [InlineData("coalesce(ts, now())", 13)]
    public void Now_in_a_stored_value_is_reported_at_the_call(string text, int offset)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.DateTime, storedValue: true).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal((ExpressionDiagnosticCodes.CurrentTimeInStoredValue, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal(
            $"Function 'now' is not allowed in a computed field, because a stored value cannot depend on the current time at character {offset + 1}.",
            diagnostic.Message);
    }

    [Theory]
    [InlineData("today()", 0, "Function 'today' is not allowed in a computed field, because a stored value cannot depend on the current time")]
    [InlineData("addDays(today(), 1)", 8, "Function 'today' is not allowed in a computed field, because a stored value cannot depend on the current time")]
    [InlineData("toDate(ts)", 0, "Function 'toDate' is not allowed in a computed field, because a stored value cannot depend on the application's time zone")]
    [InlineData("addDays(toDate(ts), 1)", 8, "Function 'toDate' is not allowed in a computed field, because a stored value cannot depend on the application's time zone")]
    public void Today_and_to_date_in_a_stored_value_are_reported_at_the_call(string text, int offset, string message)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Date, storedValue: true).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal((ExpressionDiagnosticCodes.CurrentTimeInStoredValue, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal($"{message} at character {offset + 1}.", diagnostic.Message);
    }

    [Theory]
    [InlineData("toDate(dt)", "date")]
    [InlineData("toDate(1)", "integer")]
    [InlineData("toDate('2026-10-08')", "text")]
    public void To_date_of_something_other_than_a_date_time_is_a_type_mismatch(string text, string found)
    {
        var diagnostic = CheckFails(text, ExpressionType.Date);

        Assert.Equal(ExpressionDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Contains($"found {found}", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_and_to_date_in_the_item_of_an_aggregate_of_a_stored_value_are_reported_at_the_call()
    {
        foreach (var (text, offset) in new[] { ("max(lineItems, today())", 15), ("max(lineItems, toDate(dateTime('2026-10-08T09:30:00Z')))", 15) })
        {
            var parsed = ExpressionParser.Parse(text);
            Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

            var stored = ExpressionTypeChecker.Check(parsed.Expression, CollectionScope(), ExpressionType.Date, storedValue: true);
            var notStored = ExpressionTypeChecker.Check(parsed.Expression, CollectionScope(), ExpressionType.Date);

            Assert.Equal((ExpressionDiagnosticCodes.CurrentTimeInStoredValue, offset), (stored.Diagnostic?.Code, stored.Diagnostic?.Offset));
            Assert.Equal(ExpressionType.Date, notStored.Type);
        }
    }

    [Fact]
    public void Now_in_the_item_of_an_aggregate_of_a_stored_value_is_reported_at_the_call()
    {
        var parsed = ExpressionParser.Parse("max(lineItems, now())");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var stored = ExpressionTypeChecker.Check(parsed.Expression, CollectionScope(), ExpressionType.DateTime, storedValue: true);
        var notStored = ExpressionTypeChecker.Check(parsed.Expression, CollectionScope(), ExpressionType.DateTime);

        Assert.Equal((ExpressionDiagnosticCodes.CurrentTimeInStoredValue, 15), (stored.Diagnostic?.Code, stored.Diagnostic?.Offset));
        Assert.Equal(ExpressionType.DateTime, notStored.Type);
    }

    [Theory]
    [InlineData("currentUser()", "text")]
    [InlineData("t == currentUser()", "boolean")]
    [InlineData("CURRENTUSER() != null", "boolean")]
    public void Current_user_in_a_policy_filter_is_text(string text, string type)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), policyFilter: true);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(Expected(type), result.Type);
    }

    [Theory]
    [InlineData("currentUser()", "text", false, 0)]
    [InlineData("t == currentUser()", "boolean", false, 5)]
    // A stored value is not a policy filter either.
    [InlineData("concat(t, currentUser())", "text", true, 10)]
    public void Current_user_outside_a_policy_filter_is_reported_at_the_call(string text, string type, bool storedValue, int offset)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), storedValue).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal((ExpressionDiagnosticCodes.CurrentUserOutsidePolicyFilter, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal($"Function 'currentUser' is allowed only in a policy filter at character {offset + 1}.", diagnostic.Message);
    }

    [Theory]
    [InlineData("accountableUser()", "text")]
    [InlineData("decisionTask()", "text")]
    [InlineData("t == accountableUser()", "boolean")]
    [InlineData("decisionTask() is null", "boolean")]
    [InlineData("ACCOUNTABLEUSER() != null", "boolean")]
    public void Accountability_in_a_step_set_is_text(string text, string type)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), stepSet: true);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(Expected(type), result.Type);
    }

    [Fact]
    public void Accountability_in_the_item_of_an_aggregate_of_a_step_set_is_text()
    {
        var parsed = ExpressionParser.Parse("any(lineItems, name == accountableUser())");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, CollectionScope(), ExpressionType.Boolean, stepSet: true);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Theory]
    [InlineData("accountableUser()")]
    [InlineData("decisionTask()")]
    public void Accountability_in_a_contract_is_text(string text)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected("text"), contract: true);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(Expected("text"), result.Type);
    }

    [Theory]
    [InlineData("accountableUser()", "text", "accountableUser", 0)]
    [InlineData("decisionTask()", "text", "decisionTask", 0)]
    [InlineData("t == accountableUser()", "boolean", "accountableUser", 5)]
    [InlineData("concat(t, decisionTask())", "text", "decisionTask", 10)]
    public void Accountability_outside_a_step_set_is_reported_at_the_call(string text, string type, string function, int offset)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        // The default, a stored value and a policy filter are all outside a step's set and a contract.
        var results = new[]
        {
            ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type)),
            ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), storedValue: true),
            ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), policyFilter: true),
        };

        foreach (var diagnostic in results.Select(result => result.Diagnostic))
        {
            Assert.NotNull(diagnostic);
            Assert.Equal((ExpressionDiagnosticCodes.AccountabilityOutsideStepSet, offset), (diagnostic.Code, diagnostic.Offset));
            Assert.Equal(
                $"Function '{function}' is allowed only in a module contract's expression or the set of an updateRecord or createRecord step at character {offset + 1}.",
                diagnostic.Message);
        }
    }

    [Theory]
    [InlineData("text", "t")]
    [InlineData("integer", "i")]
    [InlineData("decimal", "d")]
    [InlineData("boolean", "b")]
    [InlineData("date", "dt")]
    [InlineData("date-time", "ts")]
    [InlineData("enum", "e")]
    [InlineData("reference", "r")]
    public void Previous_in_a_contract_has_the_type_of_its_field(string type, string field)
    {
        var parsed = ExpressionParser.Parse($"previous({field})");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Null, contract: true);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(Expected(type), result.Type);
    }

    [Theory]
    [InlineData("previous(i) == i", "boolean")]
    [InlineData("PREVIOUS(i) + 1 > 0", "boolean")]
    [InlineData("previous(t) is null", "boolean")]
    [InlineData("previous(f) is null", "boolean")]
    [InlineData("previous(f) is not null", "boolean")]
    public void Previous_in_a_contract_composes_like_the_field(string text, string type)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), contract: true);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Fact]
    public void Previous_of_a_file_allows_only_a_null_check()
    {
        var parsed = ExpressionParser.Parse("previous(f) == null");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean, contract: true).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal(ExpressionDiagnosticCodes.TypeMismatch, diagnostic.Code);
    }

    [Theory]
    [InlineData("previous(lineItems) == null", 0, "'lineItems' is a child collection, previous reads only the entity's own fields at character 1.")]
    [InlineData("title == previous(missing)", 9, "Unknown field 'missing' at character 10.")]
    [InlineData("previous(1) == 1", 0, "Function 'previous' needs the name of one of the entity's own fields, outside an aggregate's item expression at character 1.")]
    [InlineData("previous(title + 'a') == 'a'", 0, "Function 'previous' needs the name of one of the entity's own fields, outside an aggregate's item expression at character 1.")]
    [InlineData("any(lineItems, previous(ok))", 15, "Function 'previous' needs the name of one of the entity's own fields, outside an aggregate's item expression at character 16.")]
    public void A_bad_previous_argument_is_reported_at_the_call(string text, int offset, string message)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, CollectionScope(), ExpressionType.Null, contract: true).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal((ExpressionDiagnosticCodes.InvalidPreviousArgument, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("previous(t)", "text", false, 0)]
    [InlineData("i == previous(i)", "boolean", false, 5)]
    // A stored value is not a contract expression either.
    [InlineData("concat(t, previous(t))", "text", true, 10)]
    public void Previous_outside_a_contract_is_reported_at_the_call(string text, string type, bool storedValue, int offset)
    {
        var parsed = ExpressionParser.Parse(text);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, _scope, Expected(type), storedValue).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal((ExpressionDiagnosticCodes.PreviousOutsideContract, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal(
            $"Function 'previous' is allowed only in a module contract's guards, core validations and effect values at character {offset + 1}.",
            diagnostic.Message);
    }

    [Fact]
    public void Previous_in_a_rule_body_is_reported_because_a_body_is_checked_on_its_own()
    {
        var parsed = ExpressionParser.Parse("previous(value) > 0");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var scope = new ExpressionScope(new Dictionary<string, ExpressionType> { ["value"] = ExpressionType.Integer });

        var diagnostic = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean).Diagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal((ExpressionDiagnosticCodes.PreviousOutsideContract, 0), (diagnostic.Code, diagnostic.Offset));
    }

    [Theory]
    [InlineData("dt == date('2026-13-45')", 6, "'2026-13-45' is not a valid date at character 7.")]
    [InlineData("dt == date('2026-02-29')", 6, "'2026-02-29' is not a valid date at character 7.")]
    [InlineData("ts == dateTime('2026-10-08T09:30:00')", 6, "'2026-10-08T09:30:00' is not a valid date-time at character 7.")]
    [InlineData("ts == dateTime('2026-10-08T09:30:00.1234567Z')", 6, "'2026-10-08T09:30:00.1234567Z' is not a valid date-time at character 7.")]
    [InlineData("dt in (date('2026-13-45'))", 7, "'2026-13-45' is not a valid date at character 8.")]
    public void A_date_literal_whose_text_is_not_valid_is_reported_at_the_call(string text, int offset, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null);

        Assert.Equal(ExpressionDiagnosticCodes.TypeMismatch, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Equal(message, diagnostic.Message);
    }

    [Theory]
    [InlineData("r.name == 'x'", 1, "'name'")]
    [InlineData("i + missing", 4, "'missing'")]
    public void Paths_are_unknown_names_in_a_scope_without_a_reference_resolver(string text, int offset, string name)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, diagnostic.Code);
        Assert.Equal(offset, diagnostic.Offset);
        Assert.Contains(name, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("r.name == 'x'")]
    [InlineData("R.Manager.NAME == 'x'")]
    [InlineData("r.manager.department.name == 'x'")]
    [InlineData("r.manager.department == r")]
    [InlineData("pr == r and r.manager.name is not null")]
    [InlineData("pr is null or t == r.name")]
    public void Paths_through_references_type_check_in_a_scope_with_a_resolver(string text)
    {
        var result = Check(text, ExpressionType.Boolean, PathScope());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Theory]
    [InlineData("r.manager.department.manager.name == 'x'", "AXC0058", 28, "The path takes 4 hops, at most 3 are allowed")]
    [InlineData("t.name == 'x'", "AXC0047", 1, "Operator '.' needs a reference, found text")]
    [InlineData("r.name.x == 'x'", "AXC0047", 6, "Operator '.' needs a reference, found text")]
    [InlineData("lower(t).name == 'x'", "AXC0047", 8, "Operator '.' needs a field path")]
    [InlineData("pr.name == 'x'", "AXC0047", 2, "Operator '.' cannot follow the parameter 'pr'. A path starts at a field")]
    [InlineData("r.missing == 'x'", "AXC0046", 1, "Unknown field 'missing' of 'department'")]
    [InlineData("r.manager.parts is null", "AXC0046", 9, "Unknown field 'parts' of 'employee'")]
    [InlineData("missing.name == 'x'", "AXC0046", 0, "Unknown field 'missing'")]
    public void Invalid_paths_are_reported_at_the_dot(string text, string code, int offset, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean, PathScope());

        Assert.Equal((code, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal($"{message} at character {offset + 1}.", diagnostic.Message);
    }

    [Theory]
    [InlineData("count(r.manager.parts)")]
    [InlineData("count(r.manager.parts, amount > 1)")]
    [InlineData("sum(r.manager.parts, amount) > 1")]
    [InlineData("any(r.manager.parts, amount > 1)")]
    [InlineData("count(r.items)")]
    [InlineData("count(r.manager.department.items) == 0")]
    public void A_scope_that_resolves_collection_paths_accepts_a_path_to_a_collection(string text)
    {
        var result = Check(text, ExpressionType.Null, CollectionPathScope());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Fact]
    public void A_path_to_a_collection_is_recorded_with_the_entity_that_holds_it()
    {
        var parsed = ExpressionParser.Parse("count(r.manager.parts)");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, CollectionPathScope(), ExpressionType.Null);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        var path = Assert.Single(result.CollectionPaths);
        Assert.Equal("employee", result.PathTargets[path]);
    }

    [Fact]
    public void A_path_to_a_collection_of_four_hops_is_too_long()
    {
        var diagnostic = CheckFails("count(r.manager.department.manager.parts)", ExpressionType.Null, CollectionPathScope());

        Assert.Equal(("AXC0058", 34), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal("The path takes 4 hops, at most 3 are allowed at character 35.", diagnostic.Message);
    }

    [Theory]
    [InlineData("count(r.manager.missing)", "AXC0046", 15, "Unknown collection 'missing' of 'employee'")]
    [InlineData("count(r.missing.parts)", "AXC0046", 7, "Unknown field 'missing' of 'department'")]
    [InlineData("count(r.parts)", "AXC0046", 7, "Unknown collection 'parts' of 'department'")]
    [InlineData("count(t.parts)", "AXC0047", 7, "Operator '.' needs a reference, found text")]
    [InlineData("count(pr.parts)", "AXC0047", 8, "Operator '.' cannot follow the parameter 'pr'. A path starts at a field")]
    public void An_invalid_path_to_a_collection_is_reported_at_the_dot(string text, string code, int offset, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null, CollectionPathScope());

        Assert.Equal((code, offset), (diagnostic.Code, diagnostic.Offset));
        Assert.Equal($"{message} at character {offset + 1}.", diagnostic.Message);
    }

    [Fact]
    public void A_scope_without_collection_paths_refuses_a_path_to_a_collection()
    {
        var diagnostic = CheckFails("count(r.manager.parts)", ExpressionType.Null, PathScope());

        Assert.Equal("AXC0046", diagnostic.Code);
    }

    [Fact]
    public void A_path_to_a_collection_is_not_a_value()
    {
        var diagnostic = CheckFails("r.manager.parts is null", ExpressionType.Boolean, CollectionPathScope());

        Assert.Equal("AXC0046", diagnostic.Code);
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
    public void A_qualified_call_resolves_to_the_rule_of_a_declared_module_ignoring_the_rule_name_case()
    {
        var ok = Check("departments:isPositive(i)", ExpressionType.Boolean, ModuleScope());

        Assert.True(ok.Succeeded, ok.Diagnostic?.Message);
        Assert.Equal("departments", Assert.Single(ok.RuleCalls.Values).Module);
    }

    [Theory]
    [InlineData("payroll:IsPositive(i)", ExpressionDiagnosticCodes.UndeclaredModuleReference,
        "The rule call 'payroll:IsPositive' names the module 'payroll', which the application does not declare. Add 'payroll' to 'modules' in application.json at character 1.")]
    [InlineData("Departments:IsPositive(i)", ExpressionDiagnosticCodes.UndeclaredModuleReference,
        "The rule call 'Departments:IsPositive' names the module 'Departments', which the application does not declare. Add 'Departments' to 'modules' in application.json at character 1.")]
    [InlineData("departments:Nope(i)", ExpressionDiagnosticCodes.UnknownFunction, "Unknown rule 'Nope' in module 'departments' at character 1.")]
    [InlineData("departments:IsPositive()", ExpressionDiagnosticCodes.WrongArgumentCount,
        "Rule 'departments:IsPositive' needs 1 argument, found 0 at character 1.")]
    [InlineData("departments:IsPositive('a')", ExpressionDiagnosticCodes.TypeMismatch,
        "Rule 'departments:IsPositive' needs integer for argument 1, found text at character 1.")]
    // A module rule is never reached by its bare name, and a module call never reaches a function or an application rule.
    [InlineData("Twice(1)", ExpressionDiagnosticCodes.UnknownFunction, "Unknown function or rule 'Twice' at character 1.")]
    [InlineData("departments:abs(1)", ExpressionDiagnosticCodes.UnknownFunction, "Unknown rule 'abs' in module 'departments' at character 1.")]
    [InlineData("departments:Own(1)", ExpressionDiagnosticCodes.UnknownFunction, "Unknown rule 'Own' in module 'departments' at character 1.")]
    public void A_wrong_qualified_call_is_reported_at_the_call(string text, string code, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null, ModuleScope());

        Assert.Equal((code, message), (diagnostic.Code, diagnostic.Message));
    }

    [Theory]
    [InlineData("payroll:IsPositive(i)")]
    [InlineData("payroll:Nope(i)")]
    public void A_module_scope_reports_a_qualified_call_to_a_module_it_does_not_name_as_an_unknown_rule(string text)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean, ModuleScope(undeclaredModuleIsUnknown: true));

        Assert.Equal(ExpressionDiagnosticCodes.UnknownFunction, diagnostic.Code);
        Assert.Contains("module.json", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("application.json", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_scope_still_resolves_a_qualified_call_to_a_module_it_names()
    {
        var ok = Check("departments:IsPositive(i)", ExpressionType.Boolean, ModuleScope(undeclaredModuleIsUnknown: true));

        Assert.True(ok.Succeeded, ok.Diagnostic?.Message);
        Assert.Equal("departments", Assert.Single(ok.RuleCalls.Values).Module);
    }

    [Fact]
    public void A_scope_built_without_modules_allows_no_qualified_call()
    {
        var scope = new ExpressionScope(
            new Dictionary<string, ExpressionType> { ["i"] = ExpressionType.Integer },
            [new ExpressionRule("IsPositive", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean) { Module = "departments" }]);

        var diagnostic = CheckFails("departments:IsPositive(i)", ExpressionType.Boolean, scope);

        Assert.Equal(ExpressionDiagnosticCodes.UnknownFunction, diagnostic.Code);
        Assert.Null(scope.DeclaresModule("departments"));
    }

    [Fact]
    public void A_rule_is_unknown_outside_a_scope_that_has_it()
    {
        Assert.Equal(ExpressionDiagnosticCodes.UnknownFunction, CheckFails("IsPositive(i)", ExpressionType.Boolean).Code);
    }

    [Theory]
    [InlineData("sum(lineItems, amount)", "decimal")]
    [InlineData("SUM(LineItems, qty)", "integer")]
    [InlineData("sum(lineItems, qty * amount)", "decimal")]
    [InlineData("sum(lineItems, null)", "integer")]
    [InlineData("count(lineItems)", "integer")]
    [InlineData("count(lineItems, ok)", "integer")]
    [InlineData("count(lineItems, qty > 1)", "integer")]
    [InlineData("any(lineItems, ok)", "boolean")]
    [InlineData("all(lineItems, ok or name == 'a')", "boolean")]
    [InlineData("min(lineItems, amount)", "decimal")]
    [InlineData("avg(lineItems, qty)", "decimal")]
    [InlineData("avg(lineItems, amount)", "decimal")]
    [InlineData("avg(lineItems, null)", "decimal")]
    [InlineData("max(lineItems, due)", "date")]
    [InlineData("count(lineItems) >= 1 and title != null", "boolean")]
    public void An_aggregate_over_a_child_collection_has_its_result_type(string text, string expected)
    {
        var result = Check(text, ExpressionType.Null, CollectionScope());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, result.Type.ToString());
    }

    [Theory]
    [InlineData("sum(title, amount)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'sum' needs a child collection or a references field for argument 1, found text at character 1.")]
    [InlineData("count(1)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'count' needs a child collection or a references field for argument 1, found integer at character 1.")]
    [InlineData("count(missing)", ExpressionDiagnosticCodes.UnknownName, "Unknown field 'missing' at character 7.")]
    [InlineData("sum(lineItems)", ExpressionDiagnosticCodes.WrongArgumentCount, "Function 'sum' needs 2 arguments, found 1 at character 1.")]
    [InlineData("count(lineItems, ok, 1)", ExpressionDiagnosticCodes.WrongArgumentCount, "Function 'count' needs 1 argument, found 3 at character 1.")]
    [InlineData("lineItems == null", ExpressionDiagnosticCodes.TypeMismatch, "Collection 'lineItems' is list<LineItem>, which only an aggregate accepts at character 1.")]
    [InlineData("lineItems is null", ExpressionDiagnosticCodes.TypeMismatch, "Collection 'lineItems' is list<LineItem>, which only an aggregate accepts at character 1.")]
    [InlineData("sum(lineItems, name)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'sum' needs a number for argument 2, found text at character 1.")]
    [InlineData("avg(lineItems, name)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'avg' needs a number for argument 2, found text at character 1.")]
    [InlineData("any(lineItems, qty)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'any' needs boolean for argument 2, found integer at character 1.")]
    [InlineData("count(lineItems, amount)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'count' needs boolean for argument 2, found decimal at character 1.")]
    [InlineData("min(lineItems, name)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'min' needs a number, date or date-time for argument 2, found text at character 1.")]
    [InlineData("max(lineItems, ok)", ExpressionDiagnosticCodes.TypeMismatch, "Function 'max' needs a number, date or date-time for argument 2, found boolean at character 1.")]
    // Inside the item expression, names are the child row's fields and nothing else.
    [InlineData("sum(lineItems, title)", ExpressionDiagnosticCodes.UnknownName, "Unknown field 'title' at character 16.")]
    [InlineData("sum(lineItems, count(lineItems))", ExpressionDiagnosticCodes.UnknownName, "Unknown field 'lineItems' at character 22.")]
    [InlineData("any(lineItems, IsPositive(qty))", ExpressionDiagnosticCodes.UnknownFunction, "Unknown function or rule 'IsPositive' at character 16.")]
    public void A_wrong_aggregate_or_collection_use_is_reported(string text, string code, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null, CollectionScope());

        Assert.Equal((code, message), (diagnostic.Code, diagnostic.Message));
    }

    [Theory]
    [InlineData("f is null")]
    [InlineData("f is not null")]
    [InlineData("f is not null and t == 'a'")]
    [InlineData("not (f is null)")]
    [InlineData("if(f is null, true, false)")]
    public void A_file_field_is_accepted_as_the_operand_of_a_null_check(string text)
    {
        var result = Check(text, ExpressionType.Boolean);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
    }

    [Theory]
    [InlineData("f == null", 1)]
    [InlineData("null != f", 9)]
    [InlineData("f == f", 1)]
    [InlineData("f in ('a', 'b')", 1)]
    [InlineData("f is null or f", 14)]
    [InlineData("coalesce(f, null) is null", 10)]
    [InlineData("coalesce(null, f) is null", 16)]
    [InlineData("f", 1)]
    [InlineData("not f", 5)]
    [InlineData("length(f) > 0", 8)]
    public void A_file_field_in_any_other_use_is_a_type_error_at_the_file(string text, int character)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean);

        Assert.Equal(
            (ExpressionDiagnosticCodes.TypeMismatch, $"A file allows only 'is null' and 'is not null' at character {character}."),
            (diagnostic.Code, diagnostic.Message));
    }

    [Fact]
    public void A_file_is_named_file_in_messages_and_never_fits_another_type()
    {
        Assert.Equal("file", ExpressionType.File.ToString());
        Assert.NotEqual(ExpressionType.Text, ExpressionType.File);
    }

    [Fact]
    public void A_collection_used_as_a_value_is_reported_at_its_name()
    {
        var diagnostic = CheckFails("lineItems == null", ExpressionType.Boolean, CollectionScope());

        Assert.Equal((ExpressionDiagnosticCodes.TypeMismatch, 0), (diagnostic.Code, diagnostic.Offset));
    }

    [Fact]
    public void A_decimal_sum_fits_a_decimal_but_not_an_integer()
    {
        Assert.True(Check("sum(lineItems, amount)", ExpressionType.Decimal, CollectionScope()).Succeeded);
        Assert.True(Check("sum(lineItems, qty)", ExpressionType.Decimal, CollectionScope()).Succeeded);
        Assert.Equal(
            ExpressionDiagnosticCodes.ResultTypeMismatch,
            CheckFails("sum(lineItems, amount)", ExpressionType.Integer, CollectionScope()).Code);
    }

    [Fact]
    public void An_aggregate_is_unknown_outside_a_scope_with_collections()
    {
        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, CheckFails("count(lineItems) > 0", ExpressionType.Boolean).Code);
    }

    [Theory]
    [InlineData("count(lines, quantity > outer.minimum)", "integer")]
    [InlineData("count(lines, OUTER.Minimum > quantity)", "integer")]
    [InlineData("any(lines, quantity > outer.minimum and outer.title == 'x')", "boolean")]
    [InlineData("count(lines, outer.dept == dept)", "integer")]
    [InlineData("count(lines, outer.dept.name == 'x')", "integer")]
    [InlineData("sum(lines, quantity * outer.minimum)", "integer")]
    [InlineData("count(lines, count(parts, outer.minimum > weight) > 0)", "integer")]
    public void An_item_expression_reads_the_outer_record(string text, string expected)
    {
        var result = Check(text, ExpressionType.Null, OuterScope());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(expected, result.Type.ToString());
    }

    [Fact]
    public void The_outer_reads_are_recorded_by_reference()
    {
        var parsed = ExpressionParser.Parse("count(lines, quantity > outer.minimum and outer.dept.name == 'x')");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);

        var result = ExpressionTypeChecker.Check(parsed.Expression, OuterScope(), ExpressionType.Null);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(["dept", "minimum"], result.OuterReads.Select(read => read.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_outer_path_follows_the_references_of_the_outer_record()
    {
        var result = Check("any(lines, outer.dept.manager.name == 'x')", ExpressionType.Boolean, OuterScope());

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        Assert.Equal(
            ["department", "employee"],
            result.PathTargets.OrderBy(pair => pair.Key.Name, StringComparer.Ordinal).Select(pair => pair.Value));
    }

    [Fact]
    public void The_word_outer_is_not_a_hop()
    {
        Assert.True(Check("any(lines, outer.dept.manager.department.name == 'x')", ExpressionType.Boolean, OuterScope()).Succeeded);

        var diagnostic = CheckFails("any(lines, outer.dept.manager.department.manager.name == 'x')", ExpressionType.Boolean, OuterScope());

        Assert.Equal(ExpressionDiagnosticCodes.TooManyHops, diagnostic.Code);
    }

    [Theory]
    [InlineData("outer.minimum > 1")]
    [InlineData("OUTER.minimum > 1")]
    [InlineData("outer.dept.name == 'x'")]
    [InlineData("outer == null")]
    [InlineData("lower(outer.title) == 'x'")]
    [InlineData("any(lines, quantity > 1) and outer.minimum > 1")]
    public void Outer_outside_an_item_expression_is_reported(string text)
    {
        var diagnostic = CheckFails(text, ExpressionType.Boolean, OuterScope());

        Assert.Equal("AXC0354", diagnostic.Code);
        Assert.Contains("'outer' is allowed only inside an aggregate's item expression", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outer_in_a_scope_that_does_not_resolve_paths_is_reported_outside_an_item_expression()
    {
        var diagnostic = CheckFails("outer.amount > 1", ExpressionType.Boolean, CollectionScope());

        Assert.Equal("AXC0354", diagnostic.Code);
    }

    [Fact]
    public void A_bare_outer_inside_an_item_expression_needs_a_field()
    {
        var diagnostic = CheckFails("count(lines, outer == null)", ExpressionType.Null, OuterScope());

        Assert.Equal(
            (ExpressionDiagnosticCodes.TypeMismatch, "'outer' needs a field, such as outer.amount at character 14."),
            (diagnostic.Code, diagnostic.Message));
    }

    [Theory]
    [InlineData("count(lines, outer.parts == null)", "AXC0047", "Collection 'parts' of the outer record is list<Part>, which an item expression cannot read at character 19.")]
    [InlineData("count(lines, outer.missing > 1)", "AXC0046", "Unknown field 'missing' of the outer record at character 19.")]
    [InlineData("count(lines, outer.quantity > 1)", "AXC0046", "Unknown field 'quantity' of the outer record at character 19.")]
    [InlineData("count(outer.dept.items)", "AXC0047", "An aggregate cannot name a collection through 'outer' at character 17.")]
    public void A_wrong_outer_read_is_reported_at_the_dot(string text, string code, string message)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null, OuterScope());

        Assert.Equal((code, message), (diagnostic.Code, diagnostic.Message));
    }

    [Fact]
    public void An_outer_path_is_an_unknown_name_in_an_outer_scope_that_does_not_resolve_paths()
    {
        var diagnostic = CheckFails("count(lines, outer.dept.name == 'x')", ExpressionType.Null, OuterScope(resolvesPaths: false));

        Assert.Equal("AXC0046", diagnostic.Code);
    }

    [Theory]
    [InlineData("count(lines, outer.pdept.name == 'x')")]
    [InlineData("any(lines, outer.pdept.manager.name == 'x')")]
    public void A_path_after_a_parameter_read_through_outer_is_reported_and_names_the_parameter(string text)
    {
        var diagnostic = CheckFails(text, ExpressionType.Null, OuterScope());

        Assert.Equal("AXC0047", diagnostic.Code);
        Assert.Contains("cannot follow the parameter 'pdept'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_field_named_outer_in_the_scope_still_reads_as_a_bare_name()
    {
        var scope = new ExpressionScope(new Dictionary<string, ExpressionType> { ["outer"] = ExpressionType.Integer });

        Assert.True(Check("outer > 1", ExpressionType.Boolean, scope).Succeeded);
    }

    /// <summary>
    /// A field <c>minimum</c>, a text <c>title</c>, a reference <c>dept</c> to a department with a name, a
    /// manager and a collection <c>items</c>, and the collections <c>lines</c> of rows with a quantity and
    /// <c>parts</c>. A line holds the collection <c>parts</c> too, with a weight.
    /// </summary>
    private static ExpressionScope OuterScope(bool resolvesPaths = true)
    {
        var parts = new ExpressionCollection("parts", "Part", new ExpressionScope(new Dictionary<string, ExpressionType> { ["weight"] = ExpressionType.Integer }));
        return new ExpressionScope(
            new Dictionary<string, ExpressionType>
            {
                ["minimum"] = ExpressionType.Integer,
                ["title"] = ExpressionType.Text,
                ["dept"] = ExpressionType.Reference("department"),
                ["pdept"] = ExpressionType.ReferenceParameter("department"),
            },
            [],
            [
                new ExpressionCollection("lines", "Line", new ExpressionScope(
                    new Dictionary<string, ExpressionType> { ["quantity"] = ExpressionType.Integer, ["dept"] = ExpressionType.Reference("department") },
                    [],
                    [parts])),
                parts,
            ],
            referenceFields: resolvesPaths
                ? (entity, field) => (entity.ToLowerInvariant(), field.ToLowerInvariant()) switch
                {
                    ("department", "name") => ExpressionType.Text,
                    ("department", "manager") => ExpressionType.Reference("employee"),
                    ("employee", "name") => ExpressionType.Text,
                    ("employee", "department") => ExpressionType.Reference("department"),
                    _ => null,
                }
                : null,
            referenceCollections: resolvesPaths
                ? (entity, name) => (entity.ToLowerInvariant(), name.ToLowerInvariant()) is ("department", "items")
                    ? new ExpressionCollection(name, "Part", new ExpressionScope(new Dictionary<string, ExpressionType> { ["weight"] = ExpressionType.Integer }))
                    : null
                : null);
    }

    /// <summary>
    /// A field <c>title</c>, the collection <c>lineItems</c> of <c>LineItem</c> rows and a rule. The
    /// rule is callable at the top level only.
    /// </summary>
    private static ExpressionScope CollectionScope() => new(
        new Dictionary<string, ExpressionType> { ["title"] = ExpressionType.Text },
        [new ExpressionRule("IsPositive", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean)],
        [
            new ExpressionCollection("lineItems", "LineItem", new ExpressionScope(new Dictionary<string, ExpressionType>
            {
                ["amount"] = ExpressionType.Decimal,
                ["qty"] = ExpressionType.Integer,
                ["ok"] = ExpressionType.Boolean,
                ["name"] = ExpressionType.Text,
                ["due"] = ExpressionType.Date,
            })),
        ]);

    /// <summary>
    /// A scope that resolves paths: <c>r</c> is a department with a name and a manager, and a
    /// manager is an employee with a name, a department and a child collection <c>parts</c>, which
    /// has no type. <c>pr</c> is a reference parameter.
    /// </summary>
    private static ExpressionScope PathScope() => new(
        new Dictionary<string, ExpressionType>
        {
            ["t"] = ExpressionType.Text,
            ["r"] = ExpressionType.Reference("department"),
            ["pr"] = ExpressionType.ReferenceParameter("department"),
        },
        referenceFields: (entity, field) => (entity.ToLowerInvariant(), field.ToLowerInvariant()) switch
        {
            ("department", "name") => ExpressionType.Text,
            ("department", "manager") => ExpressionType.Reference("employee"),
            ("employee", "name") => ExpressionType.Text,
            ("employee", "department") => ExpressionType.Reference("department"),
            _ => null,
        });

    /// <summary>The scope of <see cref="PathScope"/>, where an employee holds the collection <c>parts</c> and a department <c>items</c>.</summary>
    private static ExpressionScope CollectionPathScope() => new(
        new Dictionary<string, ExpressionType>
        {
            ["t"] = ExpressionType.Text,
            ["r"] = ExpressionType.Reference("department"),
            ["pr"] = ExpressionType.ReferenceParameter("department"),
        },
        referenceFields: (entity, field) => (entity.ToLowerInvariant(), field.ToLowerInvariant()) switch
        {
            ("department", "name") => ExpressionType.Text,
            ("department", "manager") => ExpressionType.Reference("employee"),
            ("employee", "name") => ExpressionType.Text,
            ("employee", "department") => ExpressionType.Reference("department"),
            _ => null,
        },
        referenceCollections: (entity, name) =>
            (entity.ToLowerInvariant(), name.ToLowerInvariant()) is ("employee", "parts") or ("department", "items")
                ? new ExpressionCollection(name, "Part", new ExpressionScope(new Dictionary<string, ExpressionType> { ["amount"] = ExpressionType.Decimal }))
                : null);

    /// <summary>The fields of <see cref="_scope"/>, with two rules known by their signatures.</summary>
    private static ExpressionScope ModuleScope(bool undeclaredModuleIsUnknown = false) => new(
        new Dictionary<string, ExpressionType> { ["i"] = ExpressionType.Integer },
        [
            new ExpressionRule("IsPositive", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean) { Module = "departments" },
            new ExpressionRule("Twice", [new ExpressionRuleParameter("value", ExpressionType.Decimal)], ExpressionType.Decimal) { Module = "departments" },
            new ExpressionRule("Own", [new ExpressionRuleParameter("value", ExpressionType.Integer)], ExpressionType.Boolean),
        ],
        modules: ["departments", "people"],
        undeclaredModuleIsUnknown: undeclaredModuleIsUnknown);

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
