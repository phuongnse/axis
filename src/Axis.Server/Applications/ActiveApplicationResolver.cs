using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Core.Tenancy;
using Axis.Server.Tenancy;

namespace Axis.Server.Applications;

/// <summary>
/// Turns an application name into the model of the current tenant's active release. The active
/// release is read on every call, so a new activation is served by the next call. The model is
/// rebuilt from the release's stored resources once per tenant and release, then cached.
/// </summary>
internal sealed class ActiveApplicationResolver(TenantDatabase database, ITenantContextAccessor tenants, ActiveModelCache cache)
{
    /// <summary>
    /// Returns the model of the release active under <paramref name="appName"/>, ignoring letter
    /// case, or <see langword="null"/> when no release is active under that name.
    /// </summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async Task<ApplicationModel?> ResolveAsync(string appName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appName);
        var tenant = tenants.Current
            ?? throw new InvalidOperationException("No tenant context is set. Applications are only resolved while a tenant is resolved.");

        // Names are ASCII. Checking the rule first keeps Unicode case folding, such as the Kelvin
        // sign matching 'k', from aliasing an application name, and spares the query.
        if (!ResourceNames.IsValid(appName))
        {
            return null;
        }

        var store = new ActiveReleaseStore(await database.GetConfigurationAsync(cancellationToken));
        var active = await store.FindByNameAsync(appName, cancellationToken);
        if (active is null)
        {
            return null;
        }

        if (cache.TryGet(tenant.TenantId, active.ReleaseId, out var cached))
        {
            return cached;
        }

        var release = await store.GetReleaseAsync(active.ReleaseId, cancellationToken)
            ?? throw new InvalidOperationException($"The active release '{active.ReleaseId}' could not be found.");
        return cache.GetOrAdd(tenant.TenantId, active.ReleaseId, ReleaseCompiler.BuildModel(release));
    }
}
