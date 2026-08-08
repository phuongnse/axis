using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.ValueObjects;

public enum SubjectKind { Human = 0, Service = 1 }

public readonly record struct SubjectReference(SubjectKind Kind, Guid Id)
{
    public static SubjectReference Human(Guid id) => new(SubjectKind.Human, id);
    public static SubjectReference Service(Guid id) => new(SubjectKind.Service, id);
    public static Result<SubjectReference> Create(SubjectKind kind, Guid id) =>
        id == Guid.Empty || !Enum.IsDefined(kind)
            ? Result.Failure<SubjectReference>(ErrorCodes.InvalidInput, "A valid subject is required.")
            : new SubjectReference(kind, id);
}
