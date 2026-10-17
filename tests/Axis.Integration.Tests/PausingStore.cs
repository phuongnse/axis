using System.Data.Common;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;

namespace Axis.Integration.Tests;

/// <summary>
/// A store that stops before it writes the active release until the test lets it go, so a test can
/// run another activation while this one is between its checks and its write.
/// </summary>
internal sealed class PausingStore(IActiveReleaseStore inner, TaskCompletionSource reached, TaskCompletionSource release) : IActiveReleaseStore
{
    public Task<ActiveRelease?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        inner.FindByNameAsync(name, cancellationToken);

    public Task<ActiveRelease?> FindByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
        inner.FindByApplicationIdAsync(applicationId, cancellationToken);

    public Task<Release?> GetReleaseAsync(Guid releaseId, CancellationToken cancellationToken = default) =>
        inner.GetReleaseAsync(releaseId, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, string>> GetComputedFingerprintsAsync(Guid releaseId, CancellationToken cancellationToken = default) =>
        inner.GetComputedFingerprintsAsync(releaseId, cancellationToken);

    public Task<IReadOnlyList<ActiveRelease>> ListAsync(CancellationToken cancellationToken = default) =>
        inner.ListAsync(cancellationToken);

    public Task<ActiveRelease?> FindBySitePathAsync(string path, CancellationToken cancellationToken = default) =>
        inner.FindBySitePathAsync(path, cancellationToken);

    public async Task<SetActiveReleaseResult> TrySetAsync(
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        return await inner.TrySetAsync(applicationId, name, releaseId, sitePaths, activatedAt, cancellationToken);
    }

    public async Task<SetActiveReleaseResult> TrySetInTransactionAsync(
        DbTransaction transaction,
        Guid applicationId,
        string name,
        Guid releaseId,
        IReadOnlyList<string> sitePaths,
        DateTimeOffset activatedAt,
        CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        return await inner.TrySetInTransactionAsync(transaction, applicationId, name, releaseId, sitePaths, activatedAt, cancellationToken);
    }

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        reached.TrySetResult();
        await release.Task.WaitAsync(cancellationToken);
    }
}
