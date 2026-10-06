using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the sites and pages of an application against its entities, pages and texts. A name
/// that belongs to a file which was not loaded because of its own errors is not reported again.
/// </summary>
internal static class PresentationChecker
{
    public static void Check(
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        foreach (var page in loaded.Pages)
        {
            CheckPage(page, loaded, findEntity, findPage, textKeys, diagnostics);
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

            if (findEntity(widget.Entity) is null && !loaded.UnloadedEntityNames.Contains(widget.Entity))
            {
                Report(
                    DiagnosticCodes.UnknownWidgetEntity,
                    $"The entity '{widget.Entity}' was not found. No loaded entity has that name.",
                    $"{path}/entity");
            }

            if (widget.FormPage is not { } formPage)
            {
                continue;
            }

            if (WidgetTypes.Parse(widget.Type) == WidgetType.Form)
            {
                Report(DiagnosticCodes.InvalidFormPage, "'formPage' applies only to table widgets.", $"{path}/formPage");
            }
            else if (findPage(formPage) is { } target)
            {
                var targetWidget = target.Widgets[0];
                if (WidgetTypes.Parse(targetWidget.Type) != WidgetType.Form
                    || !string.Equals(targetWidget.Entity, widget.Entity, StringComparison.OrdinalIgnoreCase))
                {
                    Report(
                        DiagnosticCodes.InvalidFormPage,
                        $"The page '{target.Name}' must hold a form widget over '{widget.Entity}', but holds a {targetWidget.Type} widget over '{targetWidget.Entity}'.",
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
