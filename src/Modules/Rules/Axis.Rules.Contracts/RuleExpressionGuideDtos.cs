namespace Axis.Rules.Contracts;

public sealed record SearchTextSegmentDto(
    string Text,
    bool IsMatch);

public sealed record SearchTextDto(
    string Text,
    IReadOnlyList<SearchTextSegmentDto> Segments);

public sealed record RuleExpressionGuideItemDto(
    RuleExpressionReferenceKind ReferenceKind,
    string ReferenceKey,
    SearchTextDto DisplayName,
    SearchTextDto Summary,
    SearchTextDto Usage,
    IReadOnlyList<SearchTextDto> Examples,
    SearchTextDto? Detail = null);

public sealed record RuleExpressionGuideSectionDto(
    string Key,
    string Title,
    string Description,
    IReadOnlyList<RuleExpressionGuideItemDto> Items);

public sealed record RuleExpressionGuideDto(
    int ExpressionLanguageVersion,
    int TotalResults,
    IReadOnlyList<RuleExpressionGuideSectionDto> Sections);

public sealed record SearchRuleExpressionGuideRequest(
    int ExpressionLanguageVersion,
    string? DefinitionKey,
    string? ContextKey,
    int? ContextSchemaVersion,
    IReadOnlyList<RuleParameterDefinitionDto> Parameters,
    string? Query,
    string Language);
