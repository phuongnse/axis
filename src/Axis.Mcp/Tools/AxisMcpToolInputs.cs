namespace Axis.Mcp.Tools;

public sealed record RuleDraftInput(
    string Label,
    IReadOnlyList<string> Types,
    bool IsRequired,
    bool AllowMultiple,
    IReadOnlyList<string> AllowedValues);

public sealed record RuleInputValue(
    string Type,
    IReadOnlyList<string> Values);

public sealed record RuleConditionNodeInput(
    string NodeId,
    string? LogicalOperator,
    string? PredicateOperator,
    RuleOperandInput? Left,
    RuleOperandInput? Right,
    IReadOnlyList<RuleConditionNodeInput> Children);

public sealed record RuleOperandInput(
    string Kind,
    string? Reference,
    RuleInputValue? Literal,
    string? Function,
    IReadOnlyList<RuleOperandInput>? Arguments);

public sealed record RuleInputDefinitionInput(
    string Key,
    string Label,
    IReadOnlyList<string> Types,
    bool IsRequired,
    bool AllowMultiple,
    IReadOnlyList<string> AllowedValues);

public sealed record CreateRuleDefinitionInput(
    string Name,
    string Description);

public sealed record SaveRuleDefinitionDraftInput(
    int ExpectedRevision,
    string Name,
    string Description,
    IReadOnlyList<RuleDraftInput> Inputs,
    RuleConditionNodeInput Condition);

public sealed record RuleRevisionInput(int ExpectedRevision);

public sealed record SearchRuleExpressionGuideInput(
    int ExpressionLanguageVersion,
    string? DefinitionKey,
    IReadOnlyList<RuleInputDefinitionInput> Inputs,
    string? Query,
    string Language);

public sealed record RuleInputMappingInput(
    string Kind,
    string? ContextKey,
    IReadOnlyList<string> LiteralValues);

public sealed record CreateRuleBindingInput(
    string DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleInputMappingInput> InputMappings,
    int Priority = 0,
    bool Enabled = true,
    string FailureBehavior = "FailClosed");

public sealed record UpdateRuleBindingInput(
    int ExpectedRevision,
    string DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleInputMappingInput> InputMappings,
    int Priority = 0,
    bool Enabled = true,
    string FailureBehavior = "FailClosed");

public sealed record BusinessObjectFieldRuleInput(
    Guid BindingId,
    Guid? Id = null);

public sealed record BusinessObjectChoiceOptionInput(
    string OptionKey,
    string Label,
    Guid? Id = null);

public sealed record BusinessObjectChoiceFieldConfigurationInput(
    string SelectionMode,
    IReadOnlyList<BusinessObjectChoiceOptionInput> Options);

public sealed record BusinessObjectFieldInput(
    string FieldKey,
    string Label,
    string FieldType = "Text",
    IReadOnlyList<BusinessObjectFieldRuleInput>? Rules = null,
    BusinessObjectChoiceFieldConfigurationInput? ChoiceConfiguration = null,
    Guid? Id = null);

public sealed record CreateBusinessObjectDefinitionInput(string Name);

public sealed record SaveUnpublishedBusinessObjectDefinitionInput(
    int ExpectedRevision,
    string Name,
    IReadOnlyList<BusinessObjectFieldInput> Fields);

public sealed record ExpectedRevisionInput(int ExpectedRevision);

public sealed record CreateBusinessObjectRecordInput(
    string IdempotencyKey,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Values = null);

public sealed record SaveBusinessObjectRecordInput(
    int ExpectedRevision,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values);

public sealed record SubmitBusinessObjectRecordInput(int ExpectedRevision);
