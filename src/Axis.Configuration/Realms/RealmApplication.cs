using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;

namespace Axis.Configuration.Realms;

/// <summary>
/// What <see cref="RealmPlanner"/> reads of an application: its sites, its identity providers and its texts.
/// <see cref="FallbackLocale"/> is the fallback locale of the first site in ordinal path order, because an application
/// has no fallback locale of its own, or the platform fallback when it has no site.
/// </summary>
public sealed record RealmApplication(
    Guid ApplicationId,
    string Name,
    IReadOnlyList<RealmApplicationSite> Sites,
    IReadOnlyList<IdentityProviderModel> Providers)
{
    /// <summary>The texts of the application: locale, then text key, then text. When resources of one locale share a key, the first wins.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>The locale whose text a label takes when the requested locale lacks the key.</summary>
    public string FallbackLocale { get; init; } = PlatformLocales.Fallback;

    /// <summary>Reads a compiled application.</summary>
    public static RealmApplication FromModel(ApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new RealmApplication(
            model.Manifest.Id,
            model.Manifest.Name,
            [.. model.Sites.Select(site => new RealmApplicationSite(site.Path, site.SignIn))],
            model.IdentityProviders)
        {
            Texts = MergeTexts(model.Texts.Select(text => (text.Locale, text.Texts))),
            FallbackLocale = model.Sites.OrderBy(site => site.Path, StringComparer.Ordinal).FirstOrDefault()?.Locales.Fallback
                ?? PlatformLocales.Fallback,
        };
    }

    /// <summary>Reads the stored resources of a release, which passed the compiler's checks.</summary>
    public static RealmApplication FromRelease(string name, Release release)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(release);

        var sites = new List<RealmApplicationSite>();
        var providers = new List<IdentityProviderModel>();
        var texts = new List<(string Locale, IReadOnlyDictionary<string, string> Texts)>();
        string? fallbackLocale = null;
        var firstSitePath = "";
        foreach (var resource in release.Resources.OrderBy(resource => resource.Path, StringComparer.Ordinal))
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
                    if (fallbackLocale is null || string.CompareOrdinal(site.Path, firstSitePath) < 0)
                    {
                        firstSitePath = site.Path;
                        fallbackLocale = site.Locales.Fallback;
                    }

                    break;
                case ResourceKinds.Text:
                    var text = root.Deserialize<TextResource>(ApplicationLoader.SerializerOptions)!;
                    texts.Add((text.Locale, text.Texts));
                    break;
            }
        }

        return new RealmApplication(release.ApplicationId, name, sites, providers)
        {
            Texts = MergeTexts(texts),
            FallbackLocale = fallbackLocale ?? PlatformLocales.Fallback,
        };
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> MergeTexts(
        IEnumerable<(string Locale, IReadOnlyDictionary<string, string> Texts)> resources)
    {
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (locale, texts) in resources)
        {
            if (!merged.TryGetValue(locale, out var keys))
            {
                merged[locale] = keys = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            foreach (var (key, text) in texts)
            {
                keys.TryAdd(key, text);
            }
        }

        return merged.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)pair.Value, StringComparer.Ordinal);
    }
}

/// <summary>A site's path and the names of the providers it allows, or null when it allows every provider.</summary>
public sealed record RealmApplicationSite(string Path, IReadOnlyList<string>? SignIn);
