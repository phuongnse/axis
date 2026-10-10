using System.Collections.Concurrent;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Processes.Work;
using Microsoft.EntityFrameworkCore;

namespace Axis.Processes.Instances;

/// <summary>
/// The compiled models of the releases instances are pinned to, per tenant and release id. Releases
/// are immutable, so an entry never goes stale. There is no eviction: the cache grows by one model
/// per release a step runs on.
/// </summary>
internal sealed class ReleaseModelCache
{
    private readonly ConcurrentDictionary<(string TenantId, Guid ReleaseId), ApplicationModel> _models = new();

    /// <summary>
    /// Returns the model of <paramref name="releaseId"/> in the item's tenant. On a miss, the release
    /// is read in the context's transaction and its model is rebuilt from its stored resources.
    /// </summary>
    /// <exception cref="InvalidOperationException">The release does not exist, or no longer compiles.</exception>
    public async Task<ApplicationModel> GetAsync(WorkItemContext context, Guid releaseId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = (context.Item.TenantId, releaseId);
        if (_models.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(context.Connection)
            .Options);
        await configuration.Database.UseTransactionAsync(context.Transaction, cancellationToken);
        var release = await new ActiveReleaseStore(configuration).GetReleaseAsync(releaseId, cancellationToken)
            ?? throw new InvalidOperationException($"The release '{releaseId}' could not be found.");
        return _models.GetOrAdd(key, ReleaseCompiler.BuildModel(release));
    }
}
