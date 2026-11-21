using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Packages;
using Axis.Tenancy;

namespace Axis.Processes.Instances;

/// <summary>
/// Checks an operation's declared capabilities against a tenant's bindings, for the worker's package call and for the publisher's run of a
/// package from Studio, see docs/reference/package-runners.md#an-applications-packages.
/// </summary>
internal static class PackageBindings
{
    /// <summary>
    /// Resolves the <c>url</c> and <c>secret</c> bindings that <paramref name="capabilities"/> declare for the tenant <paramref name="tenantId"/>.
    /// Returns <see langword="false"/> with the error when a binding is missing or of the wrong kind, or when a URL is not <c>https</c> and
    /// <paramref name="allowHttp"/> is false. The error never holds a binding's value.
    /// </summary>
    public static bool TryResolve(
        PackageCapabilities capabilities,
        string tenantId,
        IDeploymentBindings bindings,
        bool allowHttp,
        out Dictionary<string, Uri> urls,
        out Dictionary<string, Func<string>> secrets,
        [NotNullWhen(false)] out string? error)
    {
        urls = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        secrets = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in capabilities.Urls)
            {
                var value = bindings.Resolve(name).Value;
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && !allowHttp))
                {
                    error = $"Tenant '{tenantId}' binding '{name}' is not an https URL. Only Development allows http.";
                    return false;
                }

                urls[name] = uri;
            }

            foreach (var name in capabilities.Secrets)
            {
                secrets[name] = bindings.ResolveSecret(name).Reveal;
            }
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }

        error = null;
        return true;
    }
}
