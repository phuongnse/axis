namespace Axis.Rules.Contracts;

public sealed record RuleExpressionValueShapeDto(
    RuleValueType Type,
    RuleExpressionCardinality Cardinality);

public sealed record RuleReferenceContentDto(
    string DisplayName,
    string Summary,
    string Usage,
    IReadOnlyList<string> Examples);

public sealed record RuleReferenceDocumentationDto(
    IReadOnlyDictionary<string, RuleReferenceContentDto> Locales);

public sealed record RulePredicateOperatorDefinitionDto(
    RulePredicateOperator Operator,
    IReadOnlyList<RuleExpressionValueShapeDto> LeftShapes,
    IReadOnlyList<RuleExpressionValueShapeDto> RightShapes,
    bool RequiresMatchingTypes,
    RuleReferenceDocumentationDto Documentation);

public sealed record RuleExpressionFunctionParameterDto(
    IReadOnlyList<RuleValueType> AcceptedTypes,
    RuleExpressionCardinality Cardinality);

public sealed record RuleExpressionFunctionDefinitionDto(
    RuleExpressionFunction Function,
    IReadOnlyList<RuleExpressionFunctionParameterDto> Parameters,
    RuleValueType ReturnType,
    RuleExpressionCardinality ReturnCardinality,
    RuleReferenceDocumentationDto Documentation);

public sealed record RuleLogicalOperatorDefinitionDto(
    RuleLogicalOperator Operator,
    int MinimumChildren,
    int? MaximumChildren,
    RuleReferenceDocumentationDto Documentation);

public sealed record RuleOperandKindDefinitionDto(
    RuleOperandKind Kind,
    RuleReferenceDocumentationDto Documentation);

public sealed record RuleValueTypeDefinitionDto(
    RuleValueType Type,
    RuleReferenceDocumentationDto Documentation);

public sealed record RuleExpressionCardinalityDefinitionDto(
    RuleExpressionCardinality Cardinality,
    RuleReferenceDocumentationDto Documentation);

public sealed record RuleExpressionLimitDefinitionDto(
    string Key,
    int Value,
    RuleReferenceDocumentationDto Documentation);

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
    IReadOnlyList<RuleLogicalOperatorDefinitionDto> LogicalOperators,
    IReadOnlyList<RuleOperandKindDefinitionDto> OperandKinds,
    IReadOnlyList<RuleValueTypeDefinitionDto> ValueTypes,
    IReadOnlyList<RuleExpressionCardinalityDefinitionDto> Cardinalities,
    IReadOnlyList<RuleExpressionLimitDefinitionDto> LimitDefinitions,
    RuleExpressionLimitsDto Limits);
