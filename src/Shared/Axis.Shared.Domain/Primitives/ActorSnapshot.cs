namespace Axis.Shared.Domain.Primitives;

public enum ActorKind
{
    User = 0,
    ServiceIdentity = 1,
    System = 2,
}

public readonly record struct ActorSnapshot(ActorKind Kind, Guid? SubjectId, string DisplayName)
{
    public const int MaximumDisplayNameLength = 200;

    public bool IsValid =>
        Enum.IsDefined(Kind)
        && !string.IsNullOrWhiteSpace(DisplayName)
        && DisplayName == DisplayName.Trim()
        && DisplayName.Length <= MaximumDisplayNameLength
        && (Kind == ActorKind.System
            ? SubjectId is null
            : SubjectId is Guid subjectId && subjectId != Guid.Empty);

    public static ActorSnapshot User(Guid subjectId, string displayName) =>
        Create(ActorKind.User, subjectId, displayName);

    public static ActorSnapshot ServiceIdentity(Guid subjectId, string displayName) =>
        Create(ActorKind.ServiceIdentity, subjectId, displayName);

    public static ActorSnapshot System(string displayName = "Axis") =>
        Create(ActorKind.System, null, displayName);

    public static ActorSnapshot Create(ActorKind kind, Guid? subjectId, string displayName)
    {
        ActorSnapshot actor = new(kind, subjectId, displayName?.Trim() ?? string.Empty);
        if (!actor.IsValid)
            throw new ArgumentException("Actor provenance is invalid.", nameof(displayName));
        return actor;
    }
}
