using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the sites and pages of an application against its entities, pages, data sources and texts. A name
/// that belongs to a file which was not loaded because of its own errors is not reported again.
/// </summary>
internal static class PresentationChecker
{
    public static void Check(
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        foreach (var page in loaded.Pages)
        {
            CheckPage(page, loaded, findEntity, findPage, findDataSource, textKeys, diagnostics);
        }

        // Locales are compared ignoring letter case, as the text check does.
        var textLocales = new HashSet<string>(loaded.Texts.Select(text => text.Locale), StringComparer.OrdinalIgnoreCase);
        var firstFileByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var site in loaded.Sites)
        {
            CheckSite(site, loaded, findPage, textKeys, textLocales, firstFileByPath, diagnostics);
        }
    }

    private static void CheckPage(
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, path, page.Id));

        ApplicationCompiler.CheckTextKey(page.Title, page.File, page.Id, "/title", textKeys, diagnostics);

        for (var index = 0; index < page.Widgets.Count; index++)
        {
            var widget = page.Widgets[index];
            var path = $"/widgets/{index}";
            var isForm = WidgetTypes.Parse(widget.Type) == WidgetType.Form;

            // A widget shows all records of an entity or the rows of a data source, never both.
            if ((widget.Entity is null) == (widget.DataSource is null))
            {
                Report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    "A widget must name exactly one of 'entity' and 'dataSource'.",
                    path);
                continue;
            }

            // The entity the widget's records belong to: its own, or the root entity of its data
            // source. Null when the data source is unknown.
            string? recordEntity = widget.Entity;
            if (widget.Entity is { } entityName)
            {
                if (findEntity(entityName) is null && !loaded.UnloadedEntityNames.Contains(entityName))
                {
                    Report(
                        DiagnosticCodes.UnknownWidgetEntity,
                        $"The entity '{entityName}' was not found. No loaded entity has that name.",
                        $"{path}/entity");
                }
            }
            else if (isForm)
            {
                // A form edits one record of an entity, which a projection of rows cannot.
                Report(DiagnosticCodes.InvalidWidgetBinding, "'dataSource' applies only to table widgets.", $"{path}/dataSource");
                continue;
            }
            else if (findDataSource(widget.DataSource!) is { } dataSource)
            {
                recordEntity = dataSource.Entity;

                // A table has no inputs for parameter values yet, so a required one would make
                // every request for its rows fail.
                foreach (var parameter in dataSource.Parameters.Where(parameter => parameter.Required == true))
                {
                    Report(
                        DiagnosticCodes.InvalidWidgetBinding,
                        $"The data source '{dataSource.Name}' has the required parameter '{parameter.Name}'. A table can only show a data source whose parameters are all optional.",
                        $"{path}/dataSource");
                }
            }
            else if (!loaded.UnloadedDataSourceNames.Contains(widget.DataSource!))
            {
                Report(
                    DiagnosticCodes.UnknownWidgetDataSource,
                    $"The data source '{widget.DataSource}' was not found. No loaded data source has that name.",
                    $"{path}/dataSource");
            }

            if (widget.FormPage is not { } formPage)
            {
                continue;
            }

            if (isForm)
            {
                Report(DiagnosticCodes.InvalidFormPage, "'formPage' applies only to table widgets.", $"{path}/formPage");
            }
            else if (findPage(formPage) is { } target)
            {
                // Rows of a data source carry the id of their root record, so the form must be over
                // that root entity.
                var targetWidget = target.Widgets[0];
                if (WidgetTypes.Parse(targetWidget.Type) != WidgetType.Form
                    || (recordEntity is not null && !string.Equals(targetWidget.Entity, recordEntity, StringComparison.OrdinalIgnoreCase)))
                {
                    var expected = recordEntity is null ? "a form widget" : $"a form widget over '{recordEntity}'";
                    Report(
                        DiagnosticCodes.InvalidFormPage,
                        $"The page '{target.Name}' must hold {expected}, but holds a {targetWidget.Type} widget over '{targetWidget.Entity ?? targetWidget.DataSource}'.",
                        $"{path}/formPage");
                }
            }
            else if (!loaded.UnloadedPageNames.Contains(formPage))
            {
                Report(
                    DiagnosticCodes.InvalidFormPage,
                    $"The page '{formPage}' was not found. No loaded page has that name.",
                    $"{path}/formPage");
            }
        }
    }

    private static void CheckSite(
        SiteResource site,
        ApplicationLoadResult loaded,
        Func<string, PageResource?> findPage,
        IReadOnlySet<string> textKeys,
        HashSet<string> textLocales,
        Dictionary<string, string> firstFileByPath,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, site.File, path, site.Id));

        ApplicationCompiler.CheckTextKey(site.Title, site.File, site.Id, "/title", textKeys, diagnostics);

        // A reserved path is reported once and left out of the duplicate check, so one mistake
        // gives one diagnostic.
        if (SitePaths.Reserved.Contains(site.Path))
        {
            Report(DiagnosticCodes.InvalidSitePath, $"The path '{site.Path}' is reserved by the platform.", "/path");
        }
        else if (!firstFileByPath.TryAdd(site.Path, site.File))
        {
            Report(DiagnosticCodes.InvalidSitePath, $"The path '{site.Path}' is already used by '{firstFileByPath[site.Path]}'.", "/path");
        }

        var available = new HashSet<string>(site.Locales.Available, StringComparer.OrdinalIgnoreCase);
        if (!available.Contains(site.Locales.Default))
        {
            Report(DiagnosticCodes.InvalidSiteLocale, $"The default locale '{site.Locales.Default}' is not in 'available'.", "/locales/default");
        }

        if (!available.Contains(site.Locales.Fallback))
        {
            Report(DiagnosticCodes.InvalidSiteLocale, $"The fallback locale '{site.Locales.Fallback}' is not in 'available'.", "/locales/fallback");
        }

        for (var index = 0; index < site.Locales.Available.Count; index++)
        {
            var locale = site.Locales.Available[index];
            if (!textLocales.Contains(locale))
            {
                Report(DiagnosticCodes.InvalidSiteLocale, $"No text resource holds the locale '{locale}'.", $"/locales/available/{index}");
            }
        }

        for (var index = 0; index < site.Navigation.Count; index++)
        {
            var entry = site.Navigation[index];
            var path = $"/navigation/{index}";
            ApplicationCompiler.CheckTextKey(entry.Label, site.File, site.Id, $"{path}/label", textKeys, diagnostics);

            if (findPage(entry.Page) is null && !loaded.UnloadedPageNames.Contains(entry.Page))
            {
                Report(
                    DiagnosticCodes.UnknownNavigationPage,
                    $"The page '{entry.Page}' was not found. No loaded page has that name.",
                    $"{path}/page");
            }
        }
    }
}
