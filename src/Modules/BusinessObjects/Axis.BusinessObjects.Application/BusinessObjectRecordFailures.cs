using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectRecordFailures
{
    public static Result<T> MissingWorkspace<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "Current workspace scope is required.",
            BusinessObjectsProblemCodes.WorkspaceScopeRequired);

    public static Result<T> MissingUser<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "Current user scope is required.",
            BusinessObjectsProblemCodes.UserScopeRequired);

    public static Result<T> Forbidden<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "The requested product action is not allowed.",
            BusinessObjectsProblemCodes.AccessDenied);

    public static Result<T> AuthorizationUnavailable<T>() =>
        Result.Failure<T>(
            ErrorCodes.Unavailable,
            "Product authorization is temporarily unavailable.",
            BusinessObjectsProblemCodes.AuthorizationUnavailable);

    public static Result<T> NotFound<T>() =>
        Result.Failure<T>(
            ErrorCodes.NotFound,
            "Business object record was not found.",
            BusinessObjectsProblemCodes.BusinessObjectRecordNotFound);

    public static Result<T> DefinitionNotFound<T>() =>
        Result.Failure<T>(
            ErrorCodes.NotFound,
            "Published business object definition was not found.",
            BusinessObjectsProblemCodes.PublishedBusinessObjectDefinitionNotFound);

    public static Result<T> Invalid<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.InvalidInput,
            detail,
            BusinessObjectsProblemCodes.BusinessObjectRecordInvalid);

    public static Result<T> Validation<T>(IReadOnlyDictionary<string, string[]> errors) =>
        Result.FieldValidation<T>(errors);

    public static Result<T> Conflict<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            detail,
            BusinessObjectsProblemCodes.BusinessObjectRecordConflict);

    public static Result<T> IdempotencyConflict<T>() =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            "The idempotency key was already used with a different request.",
            BusinessObjectsProblemCodes.BusinessObjectRecordIdempotencyConflict);

    public static Result<T> RuleExecutionFailed<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.BusinessRule,
            detail,
            BusinessObjectsProblemCodes.BusinessObjectRecordRuleExecutionFailed);
}
