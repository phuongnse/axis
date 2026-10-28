using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;

namespace Axis.Configuration.Realms;

/// <summary>What <see cref="RealmPlanner"/> reads of an application: its sites and its identity providers.</summary>
public sealed record RealmApplication(
    Guid ApplicationId,
    string Name,
    IReadOnlyList<RealmApplicationSite> Sites,
    IReadOnlyList<IdentityProviderModel> Providers)
{
    /// <summary>Reads a compiled application.</summary>
    public static RealmApplication FromModel(ApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new RealmApplication(
            model.Manifest.Id,
            model.Manifest.Name,
            [.. model.Sites.Select(site => new RealmApplicationSite(site.Path, site.SignIn))],
            model.IdentityProviders);
    }

    /// <summary>Reads the stored resources of a release, which passed the compiler's checks.</summary>
    public static RealmApplication FromRelease(string name, Release release)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(release);

        var sites = new List<RealmApplicationSite>();
        var providers = new List<IdentityProviderModel>();
        foreach (var resource in release.Resources)
        {
            using var document = JsonDocument.Parse(resource.Content);
            var root = document.RootElement;
            if (!root.TryGetProperty("kind", out var kind))
            {
                continue;
            }

            switch (kind.GetString())
            {
                case ResourceKinds.IdentityProvider:
                    var provider = root.Deserialize<IdentityProviderResource>(ApplicationLoader.SerializerOptions)! with { File = resource.Path };
                    providers.Add(IdentityProviderChecker.Build(provider));
                    break;
                case ResourceKinds.Site:
                    var site = root.Deserialize<SiteResource>(ApplicationLoader.SerializerOptions)!;
                    sites.Add(new RealmApplicationSite(site.Path, site.SignIn));
                    break;
            }
        }

        return new RealmApplication(release.ApplicationId, name, sites, providers);
    }
}

/// <summary>A site's path and the names of the providers it allows, or null when it allows every provider.</summary>
public sealed record RealmApplicationSite(string Path, IReadOnlyList<string>? SignIn);
