using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

internal static class BusinessObjectDefinitionFailures
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

    public static Result<T> NotFound<T>() =>
        Result.Failure<T>(
            ErrorCodes.NotFound,
            "Object definition was not found.",
            BusinessObjectsProblemCodes.BusinessObjectDefinitionNotFound);

    public static Result<T> DuplicateObjectKey<T>() =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            "An object definition with this key already exists in the current workspace.",
            BusinessObjectsProblemCodes.ObjectKeyAlreadyExists);

    public static Result<T> Invalid<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.InvalidInput,
            detail,
            BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

    public static Result<T> Conflict<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            detail,
            BusinessObjectsProblemCodes.BusinessObjectDefinitionConflict);
}
