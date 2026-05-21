using Axis.WorkflowEngine.Application.Services.Condition;
using FluentAssertions;

namespace Axis.WorkflowEngine.Application.Tests.Services;

public sealed class ConditionEvaluatorTests
{
    private static Dictionary<string, object?> Branch(string label, bool isDefault = false, object? expression = null)
    {
        Dictionary<string, object?> b = new() { ["label"] = label };
        if (isDefault) b["isDefault"] = true;
        if (expression is not null) b["expression"] = expression;
        return b;
    }

    private static Dictionary<string, object?> Expr(string type, string field, object? value)
        => new() { ["type"] = type, ["field"] = field, ["value"] = value };

    // ─── Default branch ──────────────────────────────────────────────────────

    [Fact]
    public void EvaluateBranches_WhenDefaultPlacedFirst_DoesNotReturnEarlyBeforeCheckingExpressions()
    {
        // Default branch appears first; a later expression-based branch matches.
        // Expected: expression branch wins, not the default.
        List<Dictionary<string, object?>> branches =
        [
            Branch("fallback", isDefault: true),
            Branch("matched", expression: Expr("==", "status", "active"))
        ];

        Dictionary<string, object?> context = new() { ["status"] = "active" };

        string? result = ConditionEvaluator.EvaluateBranches(branches, context);

        result.Should().Be("matched");
    }

    [Fact]
    public void EvaluateBranches_WhenNoExpressionMatches_ReturnsDefaultLabel()
    {
        List<Dictionary<string, object?>> branches =
        [
            Branch("no-match", expression: Expr("==", "status", "inactive")),
            Branch("fallback", isDefault: true)
        ];

        Dictionary<string, object?> context = new() { ["status"] = "active" };

        string? result = ConditionEvaluator.EvaluateBranches(branches, context);

        result.Should().Be("fallback");
    }

    [Fact]
    public void EvaluateBranches_WhenNoBranchMatches_ReturnsNull()
    {
        List<Dictionary<string, object?>> branches =
        [
            Branch("no-match", expression: Expr("==", "status", "inactive"))
        ];

        Dictionary<string, object?> context = new() { ["status"] = "active" };

        string? result = ConditionEvaluator.EvaluateBranches(branches, context);

        result.Should().BeNull();
    }

    // ─── Numeric comparisons with missing/invalid fields ────────────────────

    [Fact]
    public void EvaluateBranches_WhenFieldMissing_NumericComparisonReturnsFalse()
    {
        // "field < 1" should NOT match when field is absent — previously returned 0 < 1 = true.
        List<Dictionary<string, object?>> branches =
        [
            Branch("matched", expression: Expr("lt", "score", 1))
        ];

        Dictionary<string, object?> context = new(); // score missing

        string? result = ConditionEvaluator.EvaluateBranches(branches, context);

        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateBranches_WhenFieldIsNonNumeric_NumericComparisonReturnsFalse()
    {
        List<Dictionary<string, object?>> branches =
        [
            Branch("matched", expression: Expr("gt", "score", 0))
        ];

        Dictionary<string, object?> context = new() { ["score"] = "not-a-number" };

        string? result = ConditionEvaluator.EvaluateBranches(branches, context);

        result.Should().BeNull();
    }

    [Fact]
    public void EvaluateBranches_WhenFieldIsValidNumber_NumericComparisonWorks()
    {
        List<Dictionary<string, object?>> branches =
        [
            Branch("high", expression: Expr("gte", "score", 90))
        ];

        Dictionary<string, object?> context = new() { ["score"] = "95" };

        string? result = ConditionEvaluator.EvaluateBranches(branches, context);

        result.Should().Be("high");
    }
}
