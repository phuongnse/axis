using System.Text;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Tenancy;

namespace Axis.Server.Studio;

/// <summary>
/// The routes that give the resource form what it chooses from, see <c>docs/reference/studio-resource-form.md#studio-api</c>.
/// <c>GET /api/studio/catalog</c> answers <c>{ modules, packages, timeZones }</c>: each installed module and package id once,
/// with its versions newest first and the label of its newest version, and the system time zones that the IANA rule accepts,
/// by offset from UTC now and then id. <c>GET /api/studio/bindings</c> answers the names of the request tenant's deployment
/// bindings by kind, <c>{ url, secret, storage }</c>, and never a value. <c>GET /api/studio/apps/{application}/relaxed</c> answers
/// the relax entries of the application's folder as <c>{ items }</c>, with the words of the module version that the folder pins
/// and the id of the module's contract. Each route answers 401 without a session and 403 without the author role, and the
/// relaxed route answers 404 for an unknown application. The routes take no path from the request.
/// </summary>
internal static class StudioCatalogEndpoints
{
    public static void MapStudioCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/catalog", CatalogAsync);
        endpoints.MapGet("/api/studio/bindings", BindingsAsync);
        endpoints.MapGet("/api/studio/apps/{application}/relaxed", RelaxedAsync);
    }

    private static async Task<IResult> CatalogAsync(
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        TimeProvider time)
    {
        if (await StudioEndpoints.RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        var now = time.GetUtcNow();
        return Results.Ok(new StudioCatalogResponse(
            [.. modules.Modules.GroupBy(module => module.Manifest.Id).OrderBy(group => group.Key, StringComparer.Ordinal).Select(Describe)],
            [.. packages.Packages.GroupBy(package => package.Manifest.Id).OrderBy(group => group.Key, StringComparer.Ordinal).Select(Describe)],
            [
                .. IanaTimeZones.All()
                    .Select(zone => new StudioCatalogTimeZone(zone.Id, (int)zone.GetUtcOffset(now).TotalMinutes))
                    .OrderBy(zone => zone.Offset)
                    .ThenBy(zone => zone.Id, StringComparer.Ordinal),
            ]));
    }

    private static StudioCatalogModule Describe(IGrouping<string, InstalledModule> versions)
    {
        var ordered = versions.OrderByDescending(module => module.Manifest.Version, Comparer<string>.Create(PackageVersions.Compare)).ToList();
        var newest = ordered[0];
        var texts = StudioReleaseChanges.TextResources(newest.Files.Select(file => file.Content));
        StudioCatalogSlot Slot(string name, string textKey) => new(name, StudioReleaseChanges.Titles(textKey, texts));
        return new StudioCatalogModule(
            versions.Key,
            Label(newest.Manifest.Label, versions.Key),
            [.. ordered.Select(module => module.Manifest.Version)],
            [.. newest.Manifest.Roles.Select(slot => Slot(slot.Name, slot.Label.TextKey))],
            [.. newest.Manifest.Connectors.Select(slot => Slot(slot.Name, slot.Label.TextKey))]);
    }

    private static StudioCatalogPackage Describe(IGrouping<string, InstalledPackage> versions)
    {
        var ordered = versions.OrderByDescending(package => package.Manifest.Version, Comparer<string>.Create(PackageVersions.Compare)).ToList();
        return new StudioCatalogPackage(
            versions.Key,
            Label(ordered[0].Manifest.Label, versions.Key),
            [.. ordered.Select(package => package.Manifest.Version)]);
    }

    private static string Label(string label, string id) => string.IsNullOrWhiteSpace(label) ? id : label;

    private static async Task<IResult> BindingsAsync(
        HttpContext httpContext,
        StudioOptions options,
        ITenantCatalog tenants,
        ITenantContextAccessor accessor)
    {
        if (await StudioEndpoints.RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        // Only the name and the kind of a binding are read: never its URL, its storage value or its secret.
        var bindings = accessor.Current is { } current ? tenants.Find(current.TenantId)?.Bindings : null;
        bindings ??= new Dictionary<string, TenantBinding>();
        List<string> Names(string kind) =>
            [.. bindings.Where(binding => binding.Value.Kind == kind).Select(binding => binding.Key).Order(StringComparer.OrdinalIgnoreCase)];
        return Results.Ok(new StudioBindingsResponse(
            Names(DeploymentBindingKinds.Url), Names(DeploymentBindingKinds.Secret), Names(DeploymentBindingKinds.Storage)));
    }

    private static async Task<IResult> RelaxedAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        // The words come from the installed module version that the compile pins, as the module resource routes find it.
        var compiled = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules);
        var pinned = modules.Modules
            .Where(module => compiled.ModulePins.Any(pin => pin.Id == module.Manifest.Id && pin.Version == module.Manifest.Version))
            .ToList();
        var manifest = StudioFolder.List(folder).FirstOrDefault(file => file.File == "application.json") is { } file
            ? Encoding.UTF8.GetString(file.Bytes)
            : null;
        return Results.Ok(new StudioFolderRelaxedResponse([
            .. StudioRelaxedWords.ReadInstalled(manifest, pinned).Select(point => new StudioFolderRelaxedPoint(
                point.Module,
                point.Point,
                point.Reason,
                point.ModuleTitle,
                point.Part,
                point.EntityTitles,
                point.Titles,
                StudioRelaxedWords.ContractId(pinned, point.Module, point.Point))),
        ]));
    }
}

internal sealed record StudioCatalogResponse(
    IReadOnlyList<StudioCatalogModule> Modules,
    IReadOnlyList<StudioCatalogPackage> Packages,
    IReadOnlyList<StudioCatalogTimeZone> TimeZones);

/// <summary>One installed module id: the label of its newest version, its versions newest first and the slots of that version.</summary>
internal sealed record StudioCatalogModule(
    string Id,
    string Label,
    IReadOnlyList<string> Versions,
    IReadOnlyList<StudioCatalogSlot> Roles,
    IReadOnlyList<StudioCatalogSlot> Connectors);

/// <summary>A role or connector slot of a module with its label in each locale whose module texts have the label.</summary>
internal sealed record StudioCatalogSlot(string Name, IReadOnlyDictionary<string, string> Titles);

internal sealed record StudioCatalogPackage(string Id, string Label, IReadOnlyList<string> Versions);

/// <summary>An IANA time zone and its offset from UTC in minutes at the time of the call.</summary>
internal sealed record StudioCatalogTimeZone(string Id, int Offset);

/// <summary>The names of the request tenant's deployment bindings by kind.</summary>
internal sealed record StudioBindingsResponse(IReadOnlyList<string> Url, IReadOnlyList<string> Secret, IReadOnlyList<string> Storage);

internal sealed record StudioFolderRelaxedResponse(IReadOnlyList<StudioFolderRelaxedPoint> Items);

/// <summary>A relax entry of a folder, as <see cref="StudioRelaxedPoint"/> with <c>ContractId</c>, the id of the contract it relaxes, or null.</summary>
internal sealed record StudioFolderRelaxedPoint(
    string Module,
    string Point,
    string Reason,
    string? ModuleTitle,
    string? Part,
    IReadOnlyDictionary<string, string>? EntityTitles,
    IReadOnlyDictionary<string, string>? Titles,
    string? ContractId);
