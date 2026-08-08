namespace Axis.BusinessObjects.Application;

public static class BusinessObjectsProblemCodes
{
    public const string WorkspaceScopeRequired = "businessObjects.workspaceScopeRequired";
    public const string UserScopeRequired = "businessObjects.userScopeRequired";
    public const string AccessDenied = "businessObjects.accessDenied";
    public const string AuthorizationUnavailable = "businessObjects.authorizationUnavailable";
    public const string BusinessObjectDefinitionNotFound = "businessObjects.definitionNotFound";
    public const string ObjectKeyAlreadyExists = "businessObjects.keyAlreadyExists";
    public const string BusinessObjectDefinitionInvalid = "businessObjects.definitionInvalid";
    public const string BusinessObjectDefinitionConflict = "businessObjects.definitionConflict";
    public const string BusinessObjectRecordNotFound = "businessObjects.recordNotFound";
    public const string PublishedBusinessObjectDefinitionNotFound = "businessObjects.publishedDefinitionNotFound";
    public const string BusinessObjectRecordInvalid = "businessObjects.recordInvalid";
    public const string BusinessObjectRecordConflict = "businessObjects.recordConflict";
    public const string BusinessObjectRecordIdempotencyConflict = "businessObjects.recordIdempotencyConflict";
    public const string BusinessObjectRecordRuleExecutionFailed = "businessObjects.recordRuleExecutionFailed";
}
