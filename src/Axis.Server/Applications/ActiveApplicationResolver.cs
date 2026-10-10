using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Core.Tenancy;
using Axis.Server.Tenancy;

namespace Axis.Server.Applications;

/// <summary>
/// Turns an application name or a site path into the model of the current tenant's active release.
/// The active release is read on every call, so a new activation is served by the next call. The
/// model is rebuilt from the release's stored resources once per tenant and release, then cached.
/// </summary>
internal sealed class ActiveApplicationResolver(TenantDatabase database, ITenantContextAccessor tenants, ActiveModelCache cache)
{
    /// <summary>
    /// Returns the model of the release active under <paramref name="appName"/>, ignoring letter
    /// case, or <see langword="null"/> when no release is active under that name.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async Task<ApplicationModel?> ResolveAsync(string appName, CancellationToken cancellationToken = default) =>
        (await ResolveReleaseAsync(appName, cancellationToken))?.Model;

    /// <summary>
    /// Returns the release active under <paramref name="appName"/>, ignoring letter case, with its
    /// model, or <see langword="null"/> when no release is active under that name.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async Task<ActiveApplication?> ResolveReleaseAsync(string appName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appName);
        var tenantId = CurrentTenantId();

        // Names are ASCII. Checking the rule first keeps Unicode case folding, such as the Kelvin
        // sign matching 'k', from aliasing an application name, and spares the query.
        if (!ResourceNames.IsValid(appName))
        {
            return null;
        }

        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var active = await store.FindByNameAsync(appName, cancellationToken);
        return active is null ? null : new ActiveApplication(active.ReleaseId, await LoadAsync(store, tenantId, active, cancellationToken));
    }

    /// <summary>Returns the models of every release active in the current tenant, in name order.</summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async Task<IReadOnlyList<ApplicationModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId();
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var models = new List<ApplicationModel>();
        foreach (var active in await store.ListAsync(cancellationToken))
        {
            models.Add(await LoadAsync(store, tenantId, active, cancellationToken));
        }

        return models;
    }

    /// <summary>
    /// Returns the site active under <paramref name="path"/>, ignoring letter case, with the model
    /// of its release, or <see langword="null"/> when no site is active under that path.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async Task<ActiveSite?> ResolveBySitePathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var tenantId = CurrentTenantId();

        // As with names, the ASCII rule comes first, so no Unicode case folding aliases a path and
        // a malformed path never reaches the database.
        if (!SitePaths.IsValid(path))
        {
            return null;
        }

        // Stored paths are lower case, and the rule makes lowering the request path exact.
        var lowerPath = path.ToLowerInvariant();
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var active = await store.FindBySitePathAsync(lowerPath, cancellationToken);
        if (active is null)
        {
            return null;
        }

        var model = await LoadAsync(store, tenantId, active, cancellationToken);
        var site = model.Sites.FirstOrDefault(candidate => string.Equals(candidate.Path, lowerPath, StringComparison.Ordinal));
        return site is null ? null : new ActiveSite(model, site);
    }

    /// <summary>
    /// Returns the model of the current tenant's release <paramref name="releaseId"/>, active or
    /// not, such as the release a process instance is pinned to.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tenant context is set, or the tenant has no such release.</exception>
    public async Task<ApplicationModel> GetReleaseModelAsync(Guid releaseId, CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId();
        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        return await LoadAsync(store, tenantId, releaseId, cancellationToken);
    }

    private string CurrentTenantId() =>
        (tenants.Current
            ?? throw new InvalidOperationException("No tenant context is set. Applications are only resolved while a tenant is resolved.")).TenantId;

    private Task<ApplicationModel> LoadAsync(ActiveReleaseStore store, string tenantId, ActiveRelease active, CancellationToken cancellationToken) =>
        LoadAsync(store, tenantId, active.ReleaseId, cancellationToken);

    // Releases are immutable and never deleted, so an active or pinned release is always stored.
    private async Task<ApplicationModel> LoadAsync(ActiveReleaseStore store, string tenantId, Guid releaseId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(tenantId, releaseId, out var cached))
        {
            return cached;
        }

        var release = await store.GetReleaseAsync(releaseId, cancellationToken)
            ?? throw new InvalidOperationException($"The release '{releaseId}' could not be found.");
        return cache.GetOrAdd(tenantId, releaseId, ReleaseCompiler.BuildModel(release));
    }
}
