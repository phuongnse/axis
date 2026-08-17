using System.ComponentModel.DataAnnotations;

namespace Axis.Identity.Contracts;

public enum SubjectKind
{
    Human = 0,
    Service = 1,
}

public readonly record struct SubjectReference(SubjectKind Kind, Guid Id)
{
    public static SubjectReference Human(Guid id) => new(SubjectKind.Human, id);
    public static SubjectReference Service(Guid id) => new(SubjectKind.Service, id);
}

public sealed record SubjectReferenceDto(
    [property: Required] SubjectKind Kind,
    [property: Required] Guid SubjectId)
{
    public static SubjectReferenceDto From(SubjectReference subject) =>
        new(subject.Kind, subject.Id);
}

/// <summary>Server-derived authenticated subject; callers must not supply it as request data.</summary>
public interface ICurrentSubject
{
    SubjectReference Subject { get; }
    string DisplayName { get; }
}
