using Axis.Data;
using Axis.Tenancy;

namespace Axis.Server.Applications;

/// <summary>Builds the bindings that activation checks a release against, from a tenant's catalog entry.</summary>
internal static class TenantActivationBindings
{
    /// <summary>
    /// The tenant's bindings as activation checks them. A secret's value stays behind, and a url
    /// binding may use http only when <paramref name="allowHttp"/> is set, which is Development.
    /// </summary>
    public static ActivationBindings For(TenantEntry tenant, bool allowHttp)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, binding) in tenant.Bindings)
        {
            // The tenant check, at startup or when the directory is read, already refused a binding without a kind,
            // or a url binding that is not an absolute URL.
            bindings[name] = new ActivationBinding(
                binding.Kind,
                binding.Kind == DeploymentBindingKinds.Url ? new Uri(binding.Url!, UriKind.Absolute) : null);
        }

        return new ActivationBindings(tenant.Id, bindings, AllowHttp: allowHttp, IsPreview: tenant.PreviewOf is not null)
        {
            PlatformProviders = tenant.PlatformProviders ?? [],
        };
    }
}
