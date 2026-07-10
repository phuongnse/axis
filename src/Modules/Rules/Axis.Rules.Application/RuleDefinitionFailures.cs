using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application;

internal static class RuleDefinitionFailures
{
    public static Result<T> MissingWorkspace<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "Current workspace scope is required.",
            RulesProblemCodes.WorkspaceScopeRequired);

    public static Result<T> MissingUser<T>() =>
        Result.Failure<T>(
            ErrorCodes.Forbidden,
            "Current user scope is required.",
            RulesProblemCodes.UserScopeRequired);

    public static Result<T> NotFound<T>() =>
        Result.Failure<T>(
            ErrorCodes.NotFound,
            "Rule definition was not found.",
            RulesProblemCodes.DefinitionNotFound);

    public static Result<T> DuplicateKey<T>() =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            "A rule definition with this key already exists in the current workspace.",
            RulesProblemCodes.DefinitionKeyAlreadyExists);

    public static Result<T> Invalid<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.InvalidInput,
            detail,
            RulesProblemCodes.DefinitionInvalid);

    public static Result<T> Conflict<T>(string detail) =>
        Result.Failure<T>(
            ErrorCodes.Conflict,
            detail,
            RulesProblemCodes.DefinitionConflict);
}
