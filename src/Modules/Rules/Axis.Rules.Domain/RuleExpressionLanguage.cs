using System.Collections.ObjectModel;

namespace Axis.Rules.Domain;

public sealed record RuleExpressionFunctionParameter(
    IReadOnlyList<RuleValueType> AcceptedTypes,
    RuleExpressionCardinality Cardinality);

public sealed record RuleExpressionFunctionDefinition(
    RuleExpressionFunction Function,
    IReadOnlyList<RuleExpressionFunctionParameter> Parameters,
    RuleValueType ReturnType,
    RuleExpressionCardinality ReturnCardinality);

public sealed record RuleExpressionValueShape(
    RuleValueType Type,
    RuleExpressionCardinality Cardinality);

public sealed record RulePredicateOperatorDefinition(
    RulePredicateOperator Operator,
    IReadOnlyList<RuleExpressionValueShape> LeftShapes,
    IReadOnlyList<RuleExpressionValueShape> RightShapes,
    bool RequiresMatchingTypes);

public static class RuleExpressionLanguage
{
    private static readonly IReadOnlyList<RuleValueType> AllTypes = Array.AsReadOnly(
        Enum.GetValues<RuleValueType>());

    public const int Version = 1;

    public static IReadOnlyList<RulePredicateOperatorDefinition> Operators { get; } =
        Array.AsReadOnly(
        [
            Binary(RulePredicateOperator.Equal, Shapes(AllTypes, RuleExpressionCardinality.Any)),
            Binary(RulePredicateOperator.NotEqual, Shapes(AllTypes, RuleExpressionCardinality.Any)),
            Binary(
                RulePredicateOperator.GreaterThan,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar)),
            Binary(
                RulePredicateOperator.GreaterThanOrEqual,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar)),
            Binary(
                RulePredicateOperator.LessThan,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar)),
            Binary(
                RulePredicateOperator.LessThanOrEqual,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar)),
            new RulePredicateOperatorDefinition(
                RulePredicateOperator.Contains,
                new ReadOnlyCollection<RuleExpressionValueShape>(
                [
                    new(RuleValueType.Text, RuleExpressionCardinality.Any),
                    .. Shapes(
                        AllTypes.Where(type => type != RuleValueType.Text).ToArray(),
                        RuleExpressionCardinality.Multiple),
                ]),
                Shapes(AllTypes, RuleExpressionCardinality.Scalar),
                RequiresMatchingTypes: true),
            Binary(
                RulePredicateOperator.StartsWith,
                Shapes([RuleValueType.Text], RuleExpressionCardinality.Scalar)),
            Binary(
                RulePredicateOperator.EndsWith,
                Shapes([RuleValueType.Text], RuleExpressionCardinality.Scalar)),
            Unary(RulePredicateOperator.IsNull),
            Unary(RulePredicateOperator.IsNotNull),
        ]);

    public static IReadOnlyList<RuleExpressionFunctionDefinition> Functions { get; } =
        Array.AsReadOnly(
        [
            Function(
                RuleExpressionFunction.IsBlank,
                [Parameter(AllTypes, RuleExpressionCardinality.Any)],
                RuleValueType.Boolean),
            Function(
                RuleExpressionFunction.Length,
                [Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar)],
                RuleValueType.Integer),
            Function(
                RuleExpressionFunction.Precision,
                [Parameter([RuleValueType.Decimal], RuleExpressionCardinality.Scalar)],
                RuleValueType.Integer),
            Function(
                RuleExpressionFunction.Scale,
                [Parameter([RuleValueType.Decimal], RuleExpressionCardinality.Scalar)],
                RuleValueType.Integer),
            Function(
                RuleExpressionFunction.Count,
                [Parameter(AllTypes, RuleExpressionCardinality.Multiple)],
                RuleValueType.Integer),
            Function(
                RuleExpressionFunction.MatchesPattern,
                [
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                ],
                RuleValueType.Boolean),
            Function(
                RuleExpressionFunction.HasFormat,
                [
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                ],
                RuleValueType.Boolean),
            Function(
                RuleExpressionFunction.ToDecimal,
                [
                    Parameter(
                        [RuleValueType.Integer, RuleValueType.Decimal],
                        RuleExpressionCardinality.Scalar),
                ],
                RuleValueType.Decimal),
        ]);

    public static RuleExpressionFunctionDefinition? Find(RuleExpressionFunction function) =>
        Functions.SingleOrDefault(definition => definition.Function == function);

    public static RulePredicateOperatorDefinition? Find(RulePredicateOperator @operator) =>
        Operators.SingleOrDefault(definition => definition.Operator == @operator);

    private static RuleExpressionFunctionParameter Parameter(
        IReadOnlyList<RuleValueType> acceptedTypes,
        RuleExpressionCardinality cardinality) =>
        new(
            new ReadOnlyCollection<RuleValueType>(acceptedTypes.ToArray()),
            cardinality);

    private static RuleExpressionFunctionDefinition Function(
        RuleExpressionFunction function,
        IReadOnlyList<RuleExpressionFunctionParameter> parameters,
        RuleValueType returnType) =>
        new(
            function,
            new ReadOnlyCollection<RuleExpressionFunctionParameter>(parameters.ToArray()),
            returnType,
            RuleExpressionCardinality.Scalar);

    private static RulePredicateOperatorDefinition Binary(
        RulePredicateOperator @operator,
        IReadOnlyList<RuleExpressionValueShape> shapes) =>
        new(@operator, shapes, shapes, RequiresMatchingTypes: true);

    private static RulePredicateOperatorDefinition Unary(RulePredicateOperator @operator) =>
        new(
            @operator,
            Shapes(AllTypes, RuleExpressionCardinality.Any),
            [],
            RequiresMatchingTypes: false);

    private static IReadOnlyList<RuleExpressionValueShape> Shapes(
        IReadOnlyList<RuleValueType> types,
        RuleExpressionCardinality cardinality) =>
        new ReadOnlyCollection<RuleExpressionValueShape>(
            types.Select(type => new RuleExpressionValueShape(type, cardinality)).ToArray());

    private static IReadOnlyList<RuleValueType> OrderedTypes() =>
        Array.AsReadOnly(
        [
            RuleValueType.Text,
            RuleValueType.Integer,
            RuleValueType.Decimal,
            RuleValueType.Date,
            RuleValueType.DateTime,
        ]);
}
