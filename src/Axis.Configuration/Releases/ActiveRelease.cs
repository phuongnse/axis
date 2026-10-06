namespace Axis.Configuration.Releases;

/// <summary>The release that is active for an application, and the manifest name it is active under.</summary>
public sealed record ActiveRelease(Guid ApplicationId, string Name, Guid ReleaseId, DateTimeOffset ActivatedAt);
