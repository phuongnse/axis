using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public abstract record RuleConditionNode
{
    protected RuleConditionNode(string nodeId)
    {
        NodeId = nodeId;
    }

    public string NodeId { get; }

    protected static Result ValidateNodeId(string nodeId) =>
        string.IsNullOrWhiteSpace(nodeId) || nodeId.Trim().Length > 64
            ? Result.Failure("Rule condition node ID is required and cannot exceed 64 characters.")
            : Result.Success();
}

public sealed record RuleConditionGroup : RuleConditionNode
{
    private RuleConditionGroup(
        string nodeId,
        RuleLogicalOperator @operator,
        IReadOnlyList<RuleConditionNode> children)
        : base(nodeId)
    {
        Operator = @operator;
        Children = children;
    }

    public RuleLogicalOperator Operator { get; }
    public IReadOnlyList<RuleConditionNode> Children { get; }

    public static Result<RuleConditionGroup> Create(
        string nodeId,
        RuleLogicalOperator @operator,
        IReadOnlyList<RuleConditionNode> children)
    {
        Result nodeIdResult = ValidateNodeId(nodeId);
        if (nodeIdResult.IsFailure)
            return Result.Failure<RuleConditionGroup>(nodeIdResult.Error);

        if (!Enum.IsDefined(@operator))
            return Result.Failure<RuleConditionGroup>("Rule logical operator is not supported.");

        int requiredChildren = @operator == RuleLogicalOperator.Not ? 1 : 0;
        if (children.Count == 0 || requiredChildren > 0 && children.Count != requiredChildren)
            return Result.Failure<RuleConditionGroup>(
                @operator == RuleLogicalOperator.Not
                    ? "A not condition must contain exactly one child."
                    : "A condition group must contain at least one child.");

        return new RuleConditionGroup(nodeId.Trim(), @operator, children.ToArray());
    }
}

public sealed record RulePredicateCondition : RuleConditionNode
{
    private RulePredicateCondition(
        string nodeId,
        RulePredicateOperator @operator,
        RuleOperand left,
        RuleOperand? right)
        : base(nodeId)
    {
        Operator = @operator;
        Left = left;
        Right = right;
    }

    public RulePredicateOperator Operator { get; }
    public RuleOperand Left { get; }
    public RuleOperand? Right { get; }

    public static Result<RulePredicateCondition> Create(
        string nodeId,
        RulePredicateOperator @operator,
        RuleOperand left,
        RuleOperand? right = null)
    {
        Result nodeIdResult = ValidateNodeId(nodeId);
        if (nodeIdResult.IsFailure)
            return Result.Failure<RulePredicateCondition>(nodeIdResult.Error);

        if (!Enum.IsDefined(@operator))
            return Result.Failure<RulePredicateCondition>("Rule predicate operator is not supported.");

        bool unary = @operator is RulePredicateOperator.IsNull or RulePredicateOperator.IsNotNull;
        if (unary && right is not null)
            return Result.Failure<RulePredicateCondition>("Unary rule predicates cannot define a right operand.");

        if (!unary && right is null)
            return Result.Failure<RulePredicateCondition>("Binary rule predicates require a right operand.");

        return new RulePredicateCondition(nodeId.Trim(), @operator, left, right);
    }
}

public sealed record RuleOperand
{
    private RuleOperand(RuleOperandKind kind, string? reference, RuleValue? literal)
    {
        Kind = kind;
        Reference = reference;
        Literal = literal;
    }

    public RuleOperandKind Kind { get; }
    public string? Reference { get; }
    public RuleValue? Literal { get; }

    public static Result<RuleOperand> Context(string path) => ReferenceOperand(RuleOperandKind.Context, path);
    public static Result<RuleOperand> Parameter(string key) => ReferenceOperand(RuleOperandKind.Parameter, key);

    public static Result<RuleOperand> LiteralValue(RuleValue value) =>
        value is null
            ? Result.Failure<RuleOperand>("Rule literal value is required.")
            : new RuleOperand(RuleOperandKind.Literal, null, value);

    private static Result<RuleOperand> ReferenceOperand(RuleOperandKind kind, string reference)
    {
        string normalized = reference?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 120)
            return Result.Failure<RuleOperand>("Rule operand reference is required and cannot exceed 120 characters.");

        return new RuleOperand(kind, normalized, null);
    }
}
