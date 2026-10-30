using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>One use of a binding name: the kind it is used as, and where.</summary>
internal sealed record BindingUse(string Kind, string File, string Path);

/// <summary>
/// Lists where an application uses each deployment binding name. No resource declares binding
/// kinds, so the kind of a name is read from where it is used: a connector's <c>baseUrl</c> and
/// <c>oauth2</c> <c>tokenUrl</c> are <c>url</c>, its other <c>auth</c> bindings are <c>secret</c>,
/// the manifest's <c>fileStorage</c> is <c>storage</c>, and an identity provider's bindings are as
/// its doc says.
/// </summary>
internal static class BindingUses
{
    public const string UrlKind = "url";
    public const string SecretKind = "secret";
    public const string StorageKind = "storage";

    /// <summary>Lists every use of a binding name with the kind it is used as, ignoring letter case.</summary>
    public static Dictionary<string, List<BindingUse>> Collect(
        IReadOnlyList<IdentityProviderResource> providers,
        ApplicationManifest? manifest,
        IReadOnlyList<ConnectorResource> connectors)
    {
        var uses = new Dictionary<string, List<BindingUse>>(StringComparer.OrdinalIgnoreCase);

        void Add(BindingReference? binding, string kind, string file, string path)
        {
            if (binding is null)
            {
                return;
            }

            if (!uses.TryGetValue(binding.Binding, out var list))
            {
                uses[binding.Binding] = list = [];
            }

            list.Add(new BindingUse(kind, file, path));
        }

        if (manifest is not null)
        {
            Add(manifest.FileStorage, StorageKind, manifest.File, "/fileStorage/binding");
        }

        foreach (var connector in connectors)
        {
            foreach (var (property, kind, binding) in BindingsOf(connector))
            {
                Add(binding, kind, connector.File, $"/{property}/binding");
            }
        }

        foreach (var provider in providers)
        {
            foreach (var (property, kind, binding) in IdentityProviderChecker.BindingsOf(provider))
            {
                Add(binding, kind, provider.File, $"/{property}/binding");
            }
        }

        return uses;
    }

    /// <summary>The bindings of a connector with the kind each is used as, and the path of each without <c>/binding</c>.</summary>
    public static IEnumerable<(string Property, string Kind, BindingReference Binding)> BindingsOf(ConnectorResource connector)
    {
        yield return ("baseUrl", UrlKind, connector.BaseUrl);
        if (connector.Auth is not { } auth)
        {
            yield break;
        }

        if (auth.Token is { } token)
        {
            yield return ("auth/token", SecretKind, token);
        }

        if (auth.Value is { } value)
        {
            yield return ("auth/value", SecretKind, value);
        }

        if (auth.TokenUrl is { } tokenUrl)
        {
            yield return ("auth/tokenUrl", UrlKind, tokenUrl);
        }

        if (auth.ClientId is { } clientId)
        {
            yield return ("auth/clientId", SecretKind, clientId);
        }

        if (auth.ClientSecret is { } clientSecret)
        {
            yield return ("auth/clientSecret", SecretKind, clientSecret);
        }
    }
}
