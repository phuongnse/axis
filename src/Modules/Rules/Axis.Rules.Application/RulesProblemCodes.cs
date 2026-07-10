namespace Axis.Rules.Application;

public static class RulesProblemCodes
{
    public const string WorkspaceScopeRequired = "rules.workspace_scope_required";
    public const string UserScopeRequired = "rules.user_scope_required";
    public const string DefinitionNotFound = "rules.definition_not_found";
    public const string DefinitionKeyAlreadyExists = "rules.definition_key_already_exists";
    public const string DefinitionInvalid = "rules.definition_invalid";
    public const string DefinitionConflict = "rules.definition_conflict";
    public const string ContextSchemaNotFound = "rules.context_schema_not_found";
    public const string EvaluationFailed = "rules.evaluation_failed";
}
