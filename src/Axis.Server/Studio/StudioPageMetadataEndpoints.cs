using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Presentation.Sites;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// <c>POST /api/studio/apps/{application}/resources/{id}/page-metadata</c> answers the page metadata an unsaved page would
/// have, so the page designer can draw it. The body is the full draft page, whose <c>id</c> and <c>kind</c> must equal those of
/// the saved file. The route compiles the folder in memory with the draft in place of the saved page, so nothing is written,
/// locked or read from the database. It answers <c>{ page, diagnostics }</c>. The page has the shape of the site's page
/// metadata, with every widget kept and full access, and each widget carries <c>path</c>, its JSON Pointer in the draft.
/// <c>diagnostics</c> are those of the draft as sent that belong to the page. A widget that does not compile is answered as
/// <c>{ type, path, placeholder: true }</c> and the rest of the tree as usual. The page is null when it cannot be drawn: when
/// a file other than the page has an error, when the page has an error outside its widgets, or when no top-level widget
/// compiles. The checks run in the order of the rest of the authoring API: session (401), author role (403), application
/// (404), then 404 for an unknown resource or one that is not a page, 415 for a content type other than
/// <c>application/json</c>, and 400 for a body that is not an object or whose <c>id</c> or <c>kind</c> differs from the
/// saved file. The route also answers an unsaved form as <c>{ widget, diagnostics }</c>, where <c>widget</c> is the metadata of
/// a <c>form</c> widget that names the form and each section, field and step carries <c>path</c>. The widget is null when the
/// compile has any error. Its diagnostics are those whose resource is the form. A resource that is neither a page nor a form
/// is 404.
/// <c>GET /api/studio/apps/{application}/modules/{module}/resources/{id}/page-metadata</c> answers <c>{ page, diagnostics }</c>
/// for a page of the module version the application pins, so Studio can draw it read-only. The <c>id</c> is the one written in
/// the module's file, found as <see cref="StudioEndpoints"/> finds a module resource. The page has the shape above and each
/// widget carries <c>path</c>. The page is null when the application's compile has any error. <c>diagnostics</c> are those of
/// the page's file. The checks run as above: session (401), author role (403), then 404 for an unknown application, a module
/// the application does not pin, an unknown resource and a resource that is not a page.
/// </summary>
internal static partial class StudioPageMetadataEndpoints
{
    // The compile of the draft as sent, then at most this many compiles of it with the widgets that failed taken out.
    private const int MaxRounds = 3;

    public static void MapStudioPageMetadataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/studio/apps/{application}/resources/{id}/page-metadata", PageMetadataAsync);
        endpoints.MapGet("/api/studio/apps/{application}/modules/{module}/resources/{id}/page-metadata", ModulePageMetadataAsync);
    }

    private static async Task<IResult> ModulePageMetadataAsync(
        string application,
        string module,
        string id,
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

        var compiled = ApplicationCompiler.Compile(folder, packages.Versions, modules.Modules);
        var (found, notFound) = StudioEndpoints.FindModuleFile(compiled, modules, module, id);
        if (found is null)
        {
            return notFound!;
        }

        if (found.Kind != "page")
        {
            return StudioEndpoints.NotFound("The resource is not a page.");
        }

        var file = $"modules/{module}/{found.Path}";
        var diagnostics = compiled.Diagnostics
            .Where(diagnostic => string.Equals(diagnostic.File, file, StringComparison.Ordinal))
            .Select(StudioDiagnostics.Map)
            .ToList();
        if (HasErrors(compiled) || compiled.Model is not { } model
            || ApplicationSites.FindPage(model, $"{module}/{found.Name}") is not { } metadata)
        {
            return Results.Ok(new StudioPageMetadataResponse(null, diagnostics));
        }

        var content = JsonNode.Parse(found.Text) as JsonObject;
        return Results.Ok(new StudioPageMetadataResponse(
            metadata with { Widgets = Merge(content?["widgets"] as JsonArray, "/widgets", metadata.Widgets, []) },
            diagnostics));
    }

    private static async Task<IResult> PageMetadataAsync(
        string application,
        string id,
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

        var (resources, folderFiles) = StudioFolder.ListWithContent(folder);
        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var pageId)
            || resources.FirstOrDefault(file => file.Id == pageId) is not { } found)
        {
            return StudioEndpoints.NotFound(StudioEndpoints.NoResource);
        }

        if (found.Kind is not ("page" or "form"))
        {
            return StudioEndpoints.NotFound("The resource is not a page or a form.");
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        JsonObject? draft;
        try
        {
            draft = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            draft = null;
        }

        if (draft is null || !SameIdentity(draft, found))
        {
            return StudioEndpoints.Problem(
                StatusCodes.Status400BadRequest,
                found.Kind == "form" ? "The body is not a valid draft form." : "The body is not a valid draft page.");
        }

        if (found.Kind == "form")
        {
            var compiled = Compile(folderFiles, found.File, draft, packages, modules);
            var formDiagnostics = compiled.Diagnostics
                .Where(diagnostic => diagnostic.ResourceId == pageId)
                .Select(StudioDiagnostics.Map)
                .ToList();
            return Results.Ok(new StudioFormMetadataResponse(DrawForm(compiled, pageId), formDiagnostics));
        }

        var first = Compile(folderFiles, found.File, draft, packages, modules);
        var diagnostics = first.Diagnostics
            .Where(diagnostic => diagnostic.ResourceId == pageId)
            .Select(StudioDiagnostics.Map)
            .ToList();
        var page = Draw(first, draft, pageId, folderFiles, found.File, packages, modules);
        return Results.Ok(new StudioPageMetadataResponse(page, diagnostics));
    }

    // The body's id and kind equal those of the saved file exactly, letter case included.
    private static bool SameIdentity(JsonObject draft, StudioFile saved)
    {
        try
        {
            return JsonNode.Parse(saved.Bytes) is JsonObject file
                && StringOf(draft, "id") is { } id && string.Equals(id, StringOf(file, "id"), StringComparison.Ordinal)
                && StringOf(draft, "kind") is { } kind && string.Equals(kind, StringOf(file, "kind"), StringComparison.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string? StringOf(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static CompilationResult Compile(
        IReadOnlyList<ResourceContent> files, string pageFile, JsonObject draft, PackageCatalog packages, ModuleCatalog modules)
    {
        var edited = files
            .Select(file => string.Equals(file.Path, pageFile, StringComparison.Ordinal) ? new ResourceContent(file.Path, draft.ToJsonString()) : file)
            .ToList();
        return ApplicationCompiler.CompileFiles(edited, packages.Versions, modules.Modules);
    }

    private static bool HasErrors(CompilationResult result) =>
        result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    // The metadata of a form widget that names the draft form, or null when the compile has any error. Without errors the
    // model keeps the order of the draft, so the position of each section, field and step is its JSON Pointer.
    private static WidgetMetadata? DrawForm(CompilationResult result, Guid formId)
    {
        if (HasErrors(result) || result.Model is not { } model
            || model.Forms.FirstOrDefault(candidate => candidate.Id == formId) is not { } form)
        {
            return null;
        }

        var entity = ApplicationSites.DescribeEntity(model, form.Entity.Name);
        IReadOnlyList<string> fields = [.. entity.Fields.Select(field => field.Name)];
        return new WidgetMetadata(
            "form",
            entity,
            null,
            WithPaths(ApplicationSites.DescribeForm(model, form.Name)),
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            new Dictionary<string, string>(),
            new WidgetAccess(["read", "create", "update", "delete"], fields, fields),
            null,
            null,
            null);
    }

    private static FormMetadata WithPaths(FormMetadata form) =>
        form with
        {
            Sections =
            [
                .. form.Sections.Select((section, index) => section with
                {
                    Path = $"/sections/{index}",
                    Fields = [.. section.Fields.Select((field, position) => field with { Path = $"/sections/{index}/fields/{position}" })],
                }),
            ],
            Steps = [.. form.Steps.Select((step, index) => step with { Path = $"/steps/{index}" })],
        };

    // The page metadata of the draft, or null when the page cannot be drawn. A widget with an error is taken out of a copy of
    // the draft, the copy is compiled again, and the widget comes back as a placeholder at its path in the draft.
    private static PageMetadata? Draw(
        CompilationResult first,
        JsonObject draft,
        Guid pageId,
        IReadOnlyList<ResourceContent> files,
        string pageFile,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var result = first;
        Dictionary<string, string>? reducedToOriginal = null;
        for (var round = 0; ; round++)
        {
            if (!HasErrors(result))
            {
                return result.Model is { } model ? Build(model, pageId, draft, removed) : null;
            }

            if (round == MaxRounds)
            {
                return null;
            }

            foreach (var error in result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                var widget = error.ResourceId == pageId ? WidgetOf(error.Path) : null;
                if (widget is null)
                {
                    return null;
                }

                if (reducedToOriginal is null)
                {
                    removed.Add(widget);
                }
                else if (reducedToOriginal.TryGetValue(widget, out var original))
                {
                    removed.Add(original);
                }
                else
                {
                    return null;
                }
            }

            var (reduced, map) = Reduce(draft, removed);
            if (reduced is null)
            {
                return null;
            }

            reducedToOriginal = map;
            result = Compile(files, pageFile, reduced, packages, modules);
        }
    }

    private static PageMetadata? Build(ApplicationModel model, Guid pageId, JsonObject draft, HashSet<string> removed)
    {
        if (model.Pages.FirstOrDefault(candidate => candidate.Id == pageId) is not { } page
            || ApplicationSites.FindPage(model, page.Name) is not { } metadata)
        {
            return null;
        }

        return metadata with { Widgets = Merge(draft["widgets"] as JsonArray, "/widgets", metadata.Widgets, removed) };
    }

    // The JSON Pointer of the innermost widget that holds the path, or null when the path is outside every widget.
    private static string? WidgetOf(string path) =>
        WidgetPointer().Match(path) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"^/widgets/\d+((/columns/\d+/widgets|/tabs/\d+/widgets|/widgets)/\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WidgetPointer();

    // A copy of the draft without the removed widgets, and the original pointer of each widget the copy keeps by its pointer in
    // the copy. A column or tab left with no widget is dropped, and a widget left with no column, tab or widget is removed too.
    // The copy is null when no top-level widget is left.
    private static (JsonObject? Reduced, Dictionary<string, string> ReducedToOriginal) Reduce(JsonObject draft, HashSet<string> removed)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var reduced = (JsonObject)draft.DeepClone();
        if (draft["widgets"] is not JsonArray widgets || ReduceList(widgets, "/widgets", "/widgets", removed, map) is not { } kept)
        {
            return (null, map);
        }

        reduced["widgets"] = kept;
        return (reduced, map);
    }

    private static JsonArray? ReduceList(
        JsonArray source, string originalBase, string reducedBase, HashSet<string> removed, Dictionary<string, string> map)
    {
        var kept = new JsonArray();
        for (var index = 0; index < source.Count; index++)
        {
            var original = $"{originalBase}/{index}";
            if (removed.Contains(original) || source[index] is not JsonObject widget)
            {
                removed.Add(original);
                continue;
            }

            var copy = (JsonObject)widget.DeepClone();
            var reduced = $"{reducedBase}/{kept.Count}";
            if (!ReduceChildren(widget, copy, original, reduced, removed, map))
            {
                removed.Add(original);
                continue;
            }

            map[reduced] = original;
            kept.Add(copy);
        }

        return kept.Count == 0 ? null : kept;
    }

    // Reduces the widget lists inside a container. False when the container held some and none is left.
    private static bool ReduceChildren(
        JsonObject widget, JsonObject copy, string original, string reduced, HashSet<string> removed, Dictionary<string, string> map)
    {
        if (widget["widgets"] is JsonArray widgets)
        {
            if (ReduceList(widgets, $"{original}/widgets", $"{reduced}/widgets", removed, map) is not { } kept)
            {
                return false;
            }

            copy["widgets"] = kept;
        }

        foreach (var name in new[] { "columns", "tabs" })
        {
            if (widget[name] is not JsonArray groups)
            {
                continue;
            }

            var keptGroups = new JsonArray();
            foreach (var (group, index) in groups.Select((group, index) => (group, index)))
            {
                if (group is JsonObject groupObject
                    && groupObject["widgets"] is JsonArray groupWidgets
                    && ReduceList(groupWidgets, $"{original}/{name}/{index}/widgets", $"{reduced}/{name}/{keptGroups.Count}/widgets", removed, map) is { } kept)
                {
                    var groupCopy = (JsonObject)groupObject.DeepClone();
                    groupCopy["widgets"] = kept;
                    keptGroups.Add(groupCopy);
                }
            }

            if (keptGroups.Count == 0)
            {
                return false;
            }

            if (keptGroups.Count < groups.Count && name == "columns")
            {
                // The widths of a columns widget must add up to 12, so the copy shares the 12 out between the columns left.
                // Only the copy has these widths: the answer takes each column's width from the draft.
                for (var column = 0; column < keptGroups.Count; column++)
                {
                    keptGroups[column]!["width"] = (12 / keptGroups.Count) + (column < 12 % keptGroups.Count ? 1 : 0);
                }
            }

            copy[name] = keptGroups;
        }

        return true;
    }

    // The widgets of the draft list in order: a removed widget as a placeholder, any other as the next built one with its path.
    private static List<PageWidgetMetadata> Merge(
        JsonArray? original, string basePath, IReadOnlyList<PageWidgetMetadata> built, HashSet<string> removed)
    {
        var result = new List<PageWidgetMetadata>();
        var next = 0;
        for (var index = 0; index < (original?.Count ?? 0); index++)
        {
            var path = $"{basePath}/{index}";
            var node = original![index] as JsonObject;
            if (removed.Contains(path) || next >= built.Count)
            {
                result.Add(new PlaceholderWidgetMetadata { DraftType = node is null ? null : StringOf(node, "type"), Path = path });
                continue;
            }

            result.Add(built[next++] switch
            {
                ColumnsWidgetMetadata columns => columns with { Path = path, Columns = MergeColumns(node, path, columns.Columns, removed) },
                TabsWidgetMetadata tabs => tabs with { Path = path, Tabs = MergeTabs(node, path, tabs.Tabs, removed) },
                SectionWidgetMetadata section => section with { Path = path, Widgets = Merge(node?["widgets"] as JsonArray, $"{path}/widgets", section.Widgets, removed) },
                var leaf => leaf with { Path = path },
            });
        }

        return result;
    }

    private static List<ColumnMetadata> MergeColumns(
        JsonObject? node, string path, IReadOnlyList<ColumnMetadata> built, HashSet<string> removed)
    {
        var result = new List<ColumnMetadata>();
        var next = 0;
        var columns = node?["columns"] as JsonArray;
        for (var index = 0; index < (columns?.Count ?? 0); index++)
        {
            var column = columns![index] as JsonObject;
            var widgets = column?["widgets"] as JsonArray;
            var widgetsPath = $"{path}/columns/{index}/widgets";
            var width = column?["width"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
            if (AllRemoved(widgets, widgetsPath, removed) || next >= built.Count)
            {
                result.Add(new ColumnMetadata(width, Merge(widgets, widgetsPath, [], removed)));
                continue;
            }

            var builtColumn = built[next++];
            result.Add(new ColumnMetadata(width == 0 ? builtColumn.Width : width, Merge(widgets, widgetsPath, builtColumn.Widgets, removed)));
        }

        return result;
    }

    private static List<TabMetadata> MergeTabs(
        JsonObject? node, string path, IReadOnlyList<TabMetadata> built, HashSet<string> removed)
    {
        var result = new List<TabMetadata>();
        var next = 0;
        var tabs = node?["tabs"] as JsonArray;
        for (var index = 0; index < (tabs?.Count ?? 0); index++)
        {
            var tab = tabs![index] as JsonObject;
            var widgets = tab?["widgets"] as JsonArray;
            var widgetsPath = $"{path}/tabs/{index}/widgets";
            if (AllRemoved(widgets, widgetsPath, removed) || next >= built.Count)
            {
                var labelKey = tab?["label"] is JsonObject label ? StringOf(label, "textKey") : null;
                result.Add(new TabMetadata(labelKey ?? string.Empty, Merge(widgets, widgetsPath, [], removed)));
                continue;
            }

            var builtTab = built[next++];
            result.Add(builtTab with { Widgets = Merge(widgets, widgetsPath, builtTab.Widgets, removed) });
        }

        return result;
    }

    private static bool AllRemoved(JsonArray? widgets, string path, HashSet<string> removed) =>
        widgets is null || Enumerable.Range(0, widgets.Count).All(index => removed.Contains($"{path}/{index}"));
}

/// <summary>The answer to a draft page metadata call: the page, or null when it cannot be drawn, and the draft's diagnostics.</summary>
internal sealed record StudioPageMetadataResponse(PageMetadata? Page, IReadOnlyList<StudioDiagnostic> Diagnostics);

/// <summary>The answer to a draft form metadata call: a form widget, or null when the compile has an error, and the form's diagnostics.</summary>
internal sealed record StudioFormMetadataResponse(WidgetMetadata? Widget, IReadOnlyList<StudioDiagnostic> Diagnostics);
