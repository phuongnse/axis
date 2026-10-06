using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Model;

namespace Axis.Server.Applications;

/// <summary>
/// The compiled models of active releases, per tenant and release id. Releases are immutable, so an
/// entry never goes stale. There is no eviction: the cache grows by one model per activated release.
/// </summary>
internal sealed class ActiveModelCache
{
    private readonly ConcurrentDictionary<(string TenantId, Guid ReleaseId), ApplicationModel> _models = new();

    public bool TryGet(string tenantId, Guid releaseId, [NotNullWhen(true)] out ApplicationModel? model) =>
        _models.TryGetValue((tenantId, releaseId), out model);

    /// <summary>Adds <paramref name="model"/> unless another one is already cached, and returns the cached one.</summary>
    public ApplicationModel GetOrAdd(string tenantId, Guid releaseId, ApplicationModel model) =>
        _models.GetOrAdd((tenantId, releaseId), model);
}
