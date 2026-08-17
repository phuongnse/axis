namespace Axis.Shared.Domain.Primitives;

public enum ActorKind
{
    User = 0,
    ServiceIdentity = 1,
    System = 2,
    Anonymous = 3,
}

public readonly record struct ActorSnapshot(ActorKind Kind, Guid? SubjectId, string DisplayName)
{
    public const int MaximumDisplayNameLength = 200;
    public const string AnonymousDisplayName = "Anonymous";
    public const string SystemDisplayName = "System";

    public bool IsValid =>
        Enum.IsDefined(Kind)
        && !string.IsNullOrWhiteSpace(DisplayName)
        && DisplayName == DisplayName.Trim()
        && DisplayName.Length <= MaximumDisplayNameLength
        && Kind switch
        {
            ActorKind.User or ActorKind.ServiceIdentity =>
                SubjectId is Guid subjectId && subjectId != Guid.Empty,
            ActorKind.Anonymous => SubjectId is null && DisplayName == AnonymousDisplayName,
            ActorKind.System => SubjectId is null && DisplayName == SystemDisplayName,
            _ => false,
        };

    public static ActorSnapshot User(Guid subjectId, string displayName) =>
        Create(ActorKind.User, subjectId, displayName);

    public static ActorSnapshot ServiceIdentity(Guid subjectId, string displayName) =>
        Create(ActorKind.ServiceIdentity, subjectId, displayName);

    public static ActorSnapshot System() =>
        Create(ActorKind.System, null, SystemDisplayName);

    public static ActorSnapshot Anonymous() =>
        Create(ActorKind.Anonymous, null, AnonymousDisplayName);

    public static ActorSnapshot Create(ActorKind kind, Guid? subjectId, string displayName)
    {
        ActorSnapshot actor = new(kind, subjectId, displayName?.Trim() ?? string.Empty);
        if (!actor.IsValid)
            throw new ArgumentException("Actor provenance is invalid.", nameof(displayName));
        return actor;
    }
}
