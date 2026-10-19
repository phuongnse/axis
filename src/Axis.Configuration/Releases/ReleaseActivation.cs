namespace Axis.Configuration.Releases;

/// <summary>One activation in the release history of an application. See <see cref="ActivationActor"/> for the actor fields.</summary>
public sealed record ReleaseActivation(
    Guid Id,
    Guid ReleaseId,
    Guid ApplicationId,
    int Number,
    string ContentHash,
    DateTimeOffset ActivatedAt,
    string Actor,
    string? ActorName,
    string? Label,
    string? Note,
    Guid? RequestId);
