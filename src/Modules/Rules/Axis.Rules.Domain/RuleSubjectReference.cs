using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public enum RuleSubjectKind
{
    Human = 0,
    Service = 1,
}

public readonly record struct RuleSubjectReference(RuleSubjectKind Kind, Guid Id)
{
    public static RuleSubjectReference Human(Guid id) => new(RuleSubjectKind.Human, id);
    public static RuleSubjectReference Service(Guid id) => new(RuleSubjectKind.Service, id);

    public static Result<RuleSubjectReference> Create(RuleSubjectKind kind, Guid id) =>
        id == Guid.Empty || !Enum.IsDefined(kind)
            ? Result.Failure<RuleSubjectReference>(ErrorCodes.InvalidInput, "A valid acting subject is required.")
            : new RuleSubjectReference(kind, id);
}
