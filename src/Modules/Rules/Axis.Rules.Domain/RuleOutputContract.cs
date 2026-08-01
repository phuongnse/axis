using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed record RuleOutputContract
{
    public RuleValueType Type { get; }
    public RuleExpressionCardinality Cardinality { get; }

    public static RuleOutputContract BooleanMatch { get; } =
        new(RuleValueType.Boolean, RuleExpressionCardinality.Scalar);

    private RuleOutputContract(RuleValueType type, RuleExpressionCardinality cardinality)
    {
        Type = type;
        Cardinality = cardinality;
    }

    public static Result<RuleOutputContract> Create(
        RuleValueType type,
        RuleExpressionCardinality cardinality)
    {
        if (!Enum.IsDefined(type))
            return Result.Failure<RuleOutputContract>("Rule output type is not supported.");

        return !Enum.IsDefined(cardinality)
            ? Result.Failure<RuleOutputContract>("Rule output cardinality is not supported.")
            : new RuleOutputContract(type, cardinality);
    }
}
