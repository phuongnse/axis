using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Presentation.Texts;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>
/// <c>GET /api/studio/apps/{application}/kinds/{kind}/resources</c> answers one page of a kind's list in Studio as
/// <c>{ items, page, pageSize, totalCount }</c>, see <c>docs/reference/studio-resource-form.md#kind-list</c>. The rows are the
/// folder's own resources of the kind and the resources of that kind in the modules it pins, each with its title in the
/// UI locale, its status and its last write. It takes <c>locale</c>, which is <c>en</c> when it is missing, and the record
/// API's <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> over the columns <c>name</c>,
/// <c>description</c>, <c>module</c>, <c>status</c>, <c>modifiedBy</c> and <c>modifiedAt</c>, and answers 400 for an
/// invalid one. With no sort the rows are in name order. A kind with no resources answers an empty page. It checks the
/// session (401), the author role (403) and the application (404), reads as <see cref="StudioActivityEndpoints.ReadAsync"/>
/// does and changes nothing.
/// </summary>
internal static class StudioKindListEndpoints
{
    private const string FallbackLocale = "en";

    public static void MapStudioKindListEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/studio/apps/{application}/kinds/{kind}/resources", ListAsync);

    // The order of a status in a sort: New, Changed, Published. A scenario has none and sorts last.
    private static readonly string[] _statuses = ["new", "changed", "published"];

    // A time filters by its day in UTC, as a date column does, and sorts by its instant. A name sorts ignoring letter case.
    private static readonly IReadOnlyList<ListColumn<StudioKindListItem>> _columns =
    [
        new("name", ListColumnKind.Text, row => row.Title, Searchable: true, Order: row => row.Title.ToUpperInvariant()),
        new("description", ListColumnKind.Text, row => row.Description, Searchable: true),
        new("module", ListColumnKind.Text, row => row.ModuleLabel, Searchable: true),
        new(
            "status",
            ListColumnKind.Enum,
            row => row.Status,
            Values: _statuses,
            Order: row => row.Status is null ? null : Array.IndexOf(_statuses, row.Status)),
        new("modifiedBy", ListColumnKind.Text, row => row.ModifiedBy),
        new(
            "modifiedAt",
            ListColumnKind.Date,
            row => row.ModifiedAt is { } time ? DateOnly.FromDateTime(time.UtcDateTime) : null,
            Order: row => row.ModifiedAt),
    ];

    private static async Task<IResult> ListAsync(
        string application,
        string kind,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules,
        TenantDatabase database,
        ITextResourceProvider texts)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<StudioKindListItem>.Read(httpContext.Request, _columns, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var locale = httpContext.Request.Query["locale"].ToString() is { Length: > 0 } requested ? requested : FallbackLocale;
        var kindName = KindName(texts, locale, kind);
        var entries = await StudioActivityEndpoints.ReadAsync(folder, packages, modules, database, httpContext.RequestAborted);

        // The sort is stable, so rows that tie on the sort column stay in name order.
        var rows = entries
            .Where(entry => string.Equals(entry.Kind, kind, StringComparison.Ordinal))
            .Select(entry => Row(entry, locale, kindName))
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToList();
        var page = query.Apply(rows);
        return Results.Ok(new StudioKindListResponse(page.Items, page.Page, page.PageSize, page.TotalCount));
    }

    private static StudioKindListItem Row(StudioActivityEntry entry, string locale, string kindName) =>
        new(
            entry.Id,
            entry.Locale is { } tag && entry.Kind == "text" ? tag : TextIn(entry.Titles, locale) ?? entry.Name ?? kindName,
            entry.Kind == "text" ? entry.Locale : null,
            TextIn(entry.Descriptions, locale),
            entry.Module,
            entry.ModuleLabel,
            entry.Status,
            entry.ModifiedBy,
            entry.ModifiedAt);

    // The text in the locale, else in en, skipping a blank one, as textIn of web/src/platform/studio.ts does.
    private static string? TextIn(IReadOnlyDictionary<string, string> texts, string locale)
    {
        foreach (var language in new[] { locale, FallbackLocale })
        {
            if (texts.TryGetValue(language, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    // The singular name of the kind in the locale, else in en, else the kind itself.
    private static string KindName(ITextResourceProvider texts, string locale, string kind)
    {
        var key = $"shell.studio.nav.kindOne.{kind}";
        foreach (var language in new[] { locale, FallbackLocale })
        {
            if (texts.GetTexts(language)?.Texts.TryGetValue(key, out var name) == true && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return kind;
    }
}

/// <summary>
/// One row of a kind's list. <c>Title</c> is the text the row shows and sorts by: a text resource's locale tag, else its
/// label, name or kind's name. <c>Locale</c> is set for a text resource only, and the browser shows the language's name for
/// it. <c>Module</c> and <c>ModuleLabel</c> are set for a module resource.
/// </summary>
internal sealed record StudioKindListItem(
    string Id,
    string Title,
    string? Locale,
    string? Description,
    string? Module,
    string? ModuleLabel,
    string? Status,
    string? ModifiedBy,
    DateTimeOffset? ModifiedAt);

/// <summary>One page of a kind's list, see <see cref="StudioKindListEndpoints"/>.</summary>
internal sealed record StudioKindListResponse(IReadOnlyList<StudioKindListItem> Items, int Page, int PageSize, long TotalCount);
