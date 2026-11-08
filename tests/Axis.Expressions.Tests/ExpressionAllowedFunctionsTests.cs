using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

/// <summary>
/// The functions a place lists are the ones the type checker accepts there, whatever the flags of the place are. A call to
/// each function that a flag restricts is checked at every combination of the flags, and the place lists the function
/// exactly when the check succeeds.
/// </summary>
public sealed class ExpressionAllowedFunctionsTests
{
    private static readonly ExpressionScope _scope = new(
        new Dictionary<string, ExpressionType> { ["amount"] = ExpressionType.Integer },
        collections: [new ExpressionCollection("lines", "Line", new ExpressionScope([]))]);

    public static TheoryData<bool, bool, bool, bool> Flags
    {
        get
        {
            var data = new TheoryData<bool, bool, bool, bool>();
            for (var bits = 0; bits < 16; bits++)
            {
                data.Add((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void A_restricted_function_is_listed_exactly_when_the_type_checker_accepts_a_call_to_it(
        bool storedValue, bool currentUser, bool contract, bool stepSet)
    {
        var calls = new (string Name, string Text)[]
        {
            ("now", "now()"),
            ("today", "today()"),
            ("toDate", "toDate(dateTime('2024-01-01T00:00:00Z'))"),
            ("currentUser", "currentUser()"),
            ("previous", "previous(amount)"),
            ("accountableUser", "accountableUser()"),
            ("decisionTask", "decisionTask()"),
            ("count", "count(lines)"),
        };

        foreach (var (name, text) in calls)
        {
            var parsed = ExpressionParser.Parse(text);
            Assert.True(parsed.Succeeded, text);

            var check = ExpressionTypeChecker.Check(
                parsed.Expression, _scope, ExpressionType.Null, storedValue, currentUser, contract, stepSet);

            Assert.Equal(check.Succeeded, check.Place!.Functions.Contains(name));
        }
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void Every_other_scalar_function_is_always_listed(bool storedValue, bool currentUser, bool contract, bool stepSet)
    {
        string[] restricted = ["now", "today", "toDate", "currentUser", "previous", "accountableUser", "decisionTask"];
        var place = new ExpressionPlace(_scope, ExpressionType.Null, storedValue, currentUser, contract, stepSet);

        Assert.All(ExpressionFunctions.Names.Except(restricted), name => Assert.Contains(name, place.Functions));
    }

    [Fact]
    public void A_scope_without_collections_lists_no_aggregate()
    {
        var place = new ExpressionPlace(new ExpressionScope([]), ExpressionType.Null, false, false, false, false);

        Assert.Empty(place.Functions.Intersect(ExpressionFunctions.AggregateNames));
        Assert.All(ExpressionFunctions.AggregateNames, name => Assert.Contains(name, new ExpressionPlace(_scope, ExpressionType.Null, false, false, false, false).Functions));
    }

    [Fact]
    public void A_failed_check_still_carries_the_place()
    {
        var parsed = ExpressionParser.Parse("missing");

        var check = ExpressionTypeChecker.Check(parsed.Expression!, _scope, ExpressionType.Boolean);

        Assert.False(check.Succeeded);
        Assert.Same(_scope, check.Place!.Scope);
        Assert.Equal(ExpressionType.Boolean, check.Place.Expected);
    }

    [Fact]
    public void A_scope_lists_its_fields_in_the_order_given_with_the_label_keys()
    {
        var scope = new ExpressionScope(
            [
                new("zeta", ExpressionType.Text),
                new("alpha", ExpressionType.Integer),
                new("Zeta", ExpressionType.Boolean),
            ],
            labelKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["alpha"] = "thing.alpha" });

        Assert.Equal(
            [("zeta", ExpressionType.Text, (string?)null), ("alpha", ExpressionType.Integer, "thing.alpha")],
            scope.Fields.Select(field => (field.Name, field.Type, field.LabelKey)));
    }
}
