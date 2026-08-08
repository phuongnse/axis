namespace Axis.Rules.Domain;

/// <summary>
/// Immutable execution configuration for one revision of a binding.
/// Published consumer contracts keep this revision so a later binding update
/// cannot silently rewrite the input mapping or exact rule reference.
/// </summary>
public sealed record RuleBindingRevision(
    int Revision,
    RuleDefinitionKey DefinitionKey,
    int DefinitionVersion,
    string TargetType,
    string TargetId,
    string UseCaseOrTrigger,
    IReadOnlyDictionary<string, RuleInputMapping> InputMappings,
    int Priority,
    bool Enabled,
    RuleBindingFailureBehavior FailureBehavior,
    RuleSubjectReference UpdatedBySubject,
    DateTime UpdatedAt);
