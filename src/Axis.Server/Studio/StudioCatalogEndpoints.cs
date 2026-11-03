using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;

namespace Axis.Server.Studio;

/// <summary>
/// Proposed: <c>GET /api/studio/catalog</c> answers the modules and the extension packages installed for Studio, so the
/// application's Settings names them by their labels and Add… offers them to choose from rather than to type. Each
/// module lists its versions, newest first, and its role and connector slots with their labels in each locale, read
/// from the module's own texts. Each package lists its label and versions. It answers to an author, as every Studio
/// route does.
/// </summary>
internal static class StudioCatalogEndpoints
{
    public static void MapStudioCatalogEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/studio/catalog", CatalogAsync);

    private static async Task<IResult> CatalogAsync(
        HttpContext httpContext, StudioOptions options, ModuleCatalog modules, PackageCatalog packages)
    {
        if (await StudioEndpoints.RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        var moduleItems = modules.Modules
            .GroupBy(module => module.Manifest.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var versions = group.OrderByDescending(module => Version(module.Manifest.Version)).ToList();
                var newest = versions[0];
                var texts = Texts(newest);
                return new StudioCatalogModule(
                    group.Key,
                    newest.Manifest.Label,
                    [.. versions.Select(module => module.Manifest.Version)],
                    [.. newest.Manifest.Roles.Select(slot => new StudioCatalogSlot(slot.Name, Titles(texts, slot.Label.TextKey)))],
                    [.. newest.Manifest.Connectors.Select(slot => new StudioCatalogSlot(slot.Name, Titles(texts, slot.Label.TextKey)))]);
            })
            .OrderBy(module => module.Label, StringComparer.CurrentCulture)
            .ToList();
        var packageItems = packages.Packages
            .GroupBy(package => package.Manifest.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var versions = group.OrderByDescending(package => Version(package.Manifest.Version)).ToList();
                return new StudioCatalogPackage(group.Key, versions[0].Manifest.Label, [.. versions.Select(package => package.Manifest.Version)]);
            })
            .OrderBy(package => package.Label ?? package.Id, StringComparer.CurrentCulture)
            .ToList();
        return Results.Ok(new StudioCatalog(moduleItems, packageItems));
    }

    private static System.Version Version(string text) =>
        System.Version.TryParse(text, out var version) ? version : new System.Version(0, 0);

    // The texts of a module's text resources by locale.
    private static Dictionary<string, Dictionary<string, string>> Texts(InstalledModule module)
    {
        var byLocale = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in module.Files)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(file.Content);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }

            if (root?["kind"]?.GetValue<string>() == "text" && root["locale"]?.GetValue<string>() is { } locale && root["texts"] is JsonObject entries)
            {
                var target = byLocale.TryGetValue(locale, out var existing) ? existing : byLocale[locale] = new(StringComparer.Ordinal);
                foreach (var (key, value) in entries)
                {
                    if (value is JsonValue text && text.TryGetValue<string>(out var line))
                    {
                        target[key] = line;
                    }
                }
            }
        }

        return byLocale;
    }

    private static Dictionary<string, string> Titles(Dictionary<string, Dictionary<string, string>> texts, string key) =>
        texts.Where(pair => pair.Value.ContainsKey(key)).ToDictionary(pair => pair.Key, pair => pair.Value[key], StringComparer.Ordinal);
}

/// <summary>Proposed: the modules and packages Studio offers.</summary>
internal sealed record StudioCatalog(IReadOnlyList<StudioCatalogModule> Modules, IReadOnlyList<StudioCatalogPackage> Packages);

/// <summary>Proposed: an installed module: its id, label, versions newest first, and its role and connector slots.</summary>
internal sealed record StudioCatalogModule(
    string Id, string Label, IReadOnlyList<string> Versions, IReadOnlyList<StudioCatalogSlot> Roles, IReadOnlyList<StudioCatalogSlot> Connectors);

/// <summary>Proposed: a slot of a module: its name and its label in each locale.</summary>
internal sealed record StudioCatalogSlot(string Name, IReadOnlyDictionary<string, string> Titles);

/// <summary>Proposed: an installed extension package: its id, its label, and its versions newest first.</summary>
internal sealed record StudioCatalogPackage(string Id, string? Label, IReadOnlyList<string> Versions);
