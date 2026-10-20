using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Templates;

namespace Axis.Server.Studio;

/// <summary>
/// <c>GET /api/studio/templates</c> lists the newest installed version of each application template, sorted by id, as
/// <c>{ items }</c>. An item is <c>{ id, version, title, description, diagnostics }</c>. The title and the description each
/// have an <c>en</c> and a <c>vi</c> text. <c>diagnostics</c> are those of compiling the template in memory without its
/// <c>template.json</c>, so a template that does not compile is still listed. The template catalog is read once at start,
/// and the route takes no path from the request. It answers 401 without a session and 403 without the author role.
/// </summary>
internal static class StudioTemplateEndpoints
{
    public static void MapStudioTemplateEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/studio/templates", ListAsync);

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        StudioOptions options,
        TemplateCatalog templates,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        if (await StudioEndpoints.RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        var items = templates.Newest
            .Select(template => new StudioTemplateItem(
                template.Manifest.Id,
                template.Manifest.Version,
                new StudioTemplateText(template.Manifest.Title.En, template.Manifest.Title.Vi),
                new StudioTemplateText(template.Manifest.Description.En, template.Manifest.Description.Vi),
                StudioDiagnostics.CompileFiles(template.Files, packages, modules)))
            .ToList();
        return Results.Ok(new StudioTemplateListResponse(items));
    }
}

internal sealed record StudioTemplateListResponse(IReadOnlyList<StudioTemplateItem> Items);

internal sealed record StudioTemplateItem(
    string Id,
    string Version,
    StudioTemplateText Title,
    StudioTemplateText Description,
    IReadOnlyList<StudioDiagnostic> Diagnostics);

internal sealed record StudioTemplateText(string En, string Vi);
