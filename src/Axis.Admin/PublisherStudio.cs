using Axis.Studio;

namespace Axis.Admin;

/// <summary>
/// The Studio folders the publisher reads and writes: the options it loaded with the server's rules and the write lock
/// that a write to a folder holds. The lock is null when <c>ConnectionStrings:Platform</c> is not set. See
/// <see href="docs/reference/host-settings.md#studio-settings">Studio settings</see>.
/// </summary>
internal sealed class PublisherStudio(StudioOptions options, IStudioWriteLock? writeLock)
{
    public StudioOptions Options { get; } = options;

    public IStudioWriteLock? WriteLock { get; } = writeLock;

    /// <summary>The folder of the application <paramref name="application"/>, found as Studio finds it, or null.</summary>
    public string? Find(string? tenantId, string application) => StudioFolder.Find(Options, tenantId, application);

    /// <summary>The folder that holds the revisions of the tenant's folders, or null when none are kept.</summary>
    public string? RevisionsPath(string? tenantId) => Options.ForTenant(tenantId).RevisionsPath;
}
