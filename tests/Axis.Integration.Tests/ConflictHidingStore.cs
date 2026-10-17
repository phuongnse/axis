using System.Data.Common;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;

namespace Axis.Integration.Tests;

/// <summary>
/// A store that never finds an active release by name or site path, as when another activation
/// takes the name or path concurrently.
/// </summary>
internal sealed class ConflictHidingStore(IActiveReleaseStore inner) : IActiveReleaseStore
{
    public Task<ActiveRelease?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActiveRelease?>(null);

    public Task<ActiveRelease?> FindByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
        inner.FindByApplicationIdAsync(applicationId, cancellationToken);

    public Task<Release?> GetReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default) =>
        inner.GetReleaseAsync(releaseId, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, string>> GetComputedFingerprintsAsync(Guid releaseId, CancellationToken cancellationToken = default) =>
        inner.GetComputedFingerprintsAsync(releaseId, cancellationToken);

    public Task<IReadOnlyList<ActiveRelease>> ListAsync(CancellationToken cancellationToken = default) =>
        inner.ListAsync(cancellationToken);

    public Task<ActiveRelease?> FindBySitePathAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<ActiveRelease?>(null);

    public Task<SetActiveReleaseResult> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default) =>
        inner.TrySetAsync(applicationId, name, releaseId, sitePaths, activatedAt, cancellationToken);

    public Task<SetActiveReleaseResult> TrySetInTransactionAsync(
        DbTransaction transaction,
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default) =>
        inner.TrySetInTransactionAsync(transaction, applicationId, name, releaseId, sitePaths, activatedAt, cancellationToken);
}
