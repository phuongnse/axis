namespace Axis.Rules.Contracts;

public sealed record RuleExpressionValueShapeDto(
    RuleValueType Type,
    RuleExpressionCardinality Cardinality);

public sealed record RulePredicateOperatorDefinitionDto(
    RulePredicateOperator Operator,
    IReadOnlyList<RuleExpressionValueShapeDto> LeftShapes,
    IReadOnlyList<RuleExpressionValueShapeDto> RightShapes,
    bool RequiresMatchingTypes);

public sealed record RuleExpressionFunctionParameterDto(
    IReadOnlyList<RuleValueType> AcceptedTypes,
    RuleExpressionCardinality Cardinality);

public sealed record RuleExpressionFunctionDefinitionDto(
    RuleExpressionFunction Function,
    IReadOnlyList<RuleExpressionFunctionParameterDto> Parameters,
    RuleValueType ReturnType,
    RuleExpressionCardinality ReturnCardinality);

public sealed record RuleExpressionLimitsDto(
    int MaxDepth,
    int MaxNodes,
    int MaxFunctionCalls,
    int MaxParameters,
    int MaxExecutionSteps);

public sealed record RuleExpressionLanguageDto(
    int Version,
    IReadOnlyList<RulePredicateOperatorDefinitionDto> Operators,
    IReadOnlyList<RuleExpressionFunctionDefinitionDto> Functions,
    RuleExpressionLimitsDto Limits);
