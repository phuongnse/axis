using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Application;

internal static class ObjectDefinitionFailures
{
    public static Result<T> MissingWorkspace<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "Current workspace scope is required.",
            ObjectsProblemCodes.WorkspaceScopeRequired);

    public static Result<T> MissingUser<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "Current user scope is required.",
            ObjectsProblemCodes.UserScopeRequired);

    public static Result<T> NotFound<T>() =>
        Result.Failure<T>(
            ErrorCodes.NotFound,
            "Object definition was not found.",
            ObjectsProblemCodes.ObjectDefinitionNotFound);

    public static Result<T> DuplicateObjectKey<T>() =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            "An object definition with this key already exists in the current workspace.",
            ObjectsProblemCodes.ObjectKeyAlreadyExists);

    public static Result<T> Invalid<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.InvalidInput,
            detail,
            ObjectsProblemCodes.ObjectDefinitionInvalid);

    public static Result<T> Conflict<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            detail,
            ObjectsProblemCodes.ObjectDefinitionConflict);
}
