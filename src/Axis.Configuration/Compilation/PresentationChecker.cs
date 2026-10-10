using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the forms, sites and pages of an application against its entities, pages, data sources,
/// forms and texts. A name that belongs to a file which was not loaded because of its own errors is
/// not reported again.
/// </summary>
internal static class PresentationChecker
{
    public static void Check(
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, FormResource?> findForm,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        foreach (var form in loaded.Forms)
        {
            CheckForm(form, loaded, findEntity, textKeys, diagnostics);
        }

        foreach (var page in loaded.Pages)
        {
            CheckPage(page, loaded, findEntity, findPage, findDataSource, findForm, textKeys, diagnostics);
        }

        // Locales are compared ignoring letter case, as the text check does.
        var textLocales = new HashSet<string>(loaded.Texts.Select(text => text.Locale), StringComparer.OrdinalIgnoreCase);
        var firstFileByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var site in loaded.Sites)
        {
            CheckSite(site, loaded, findPage, textKeys, textLocales, firstFileByPath, diagnostics);
        }
    }

    /// <summary>
    /// Checks a form: its entity exists, every section title has a text key, and each field is a
    /// field of the entity listed once across all sections, ignoring letter case.
    /// </summary>
    private static void CheckForm(
        FormResource form,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, form.File, path, form.Id));

        var entity = findEntity(form.Entity);
        if (entity is null && !loaded.UnloadedEntityNames.Contains(form.Entity))
        {
            Report(
                DiagnosticCodes.UnknownFormEntity,
                $"The entity '{form.Entity}' was not found. No loaded entity has that name.",
                "/entity");
        }

        var firstPathByField = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var sectionIndex = 0; sectionIndex < form.Sections.Count; sectionIndex++)
        {
            var section = form.Sections[sectionIndex];
            ApplicationCompiler.CheckTextKey(section.Title, form.File, form.Id, $"/sections/{sectionIndex}/title", textKeys, diagnostics);

            // Without the entity there is nothing to check the fields against.
            if (entity is null)
            {
                continue;
            }

            for (var fieldIndex = 0; fieldIndex < section.Fields.Count; fieldIndex++)
            {
                var name = section.Fields[fieldIndex].Field;
                var path = $"/sections/{sectionIndex}/fields/{fieldIndex}/field";
                if (ApplicationCompiler.FindField(entity, name) is null)
                {
                    Report(
                        DiagnosticCodes.UnknownFormField,
                        $"The field '{name}' was not found. The entity '{entity.Name}' has no field with that name.",
                        path);
                }
                else if (!firstPathByField.TryAdd(name, path))
                {
                    Report(
                        DiagnosticCodes.DuplicateFormField,
                        $"The field '{name}' is already listed at '{firstPathByField[name]}'. A form lists each field once.",
                        path);
                }
            }
        }
    }

    private static void CheckPage(
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, FormResource?> findForm,
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

            if (isForm && widget.DataSource is not null)
            {
                // A form edits one record of an entity, which a projection of rows cannot.
                Report(DiagnosticCodes.InvalidWidgetBinding, "'dataSource' applies only to table widgets.", $"{path}/dataSource");
                continue;
            }

            if (isForm && (widget.Form is null) == (widget.Entity is null))
            {
                // A form lays out the fields of its own entity, so naming an entity as well says nothing new.
                Report(DiagnosticCodes.InvalidWidgetBinding, "A form widget must name exactly one of 'form' and 'entity'.", path);
                continue;
            }

            if (!isForm && widget.Form is not null)
            {
                Report(DiagnosticCodes.InvalidWidgetBinding, "'form' applies only to form widgets.", $"{path}/form");
                continue;
            }

            // A table shows all records of an entity or the rows of a data source, never both.
            if (!isForm && (widget.Entity is null) == (widget.DataSource is null))
            {
                Report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    "A widget must name exactly one of 'entity' and 'dataSource'.",
                    path);
                continue;
            }

            // The entity the widget's records belong to: its own, that of its form, or the root
            // entity of its data source. Null when the form or data source is unknown.
            string? recordEntity = widget.Entity;

            // Whether the widget shows the groups of a data source rather than records.
            var grouped = false;
            if (widget.Form is { } formName)
            {
                if (findForm(formName) is { } form)
                {
                    recordEntity = form.Entity;
                }
                else if (!loaded.UnloadedFormNames.Contains(formName))
                {
                    Report(
                        DiagnosticCodes.UnknownWidgetForm,
                        $"The form '{formName}' was not found. No loaded form has that name.",
                        $"{path}/form");
                }
            }
            else if (widget.Entity is { } entityName)
            {
                if (findEntity(entityName) is null && !loaded.UnloadedEntityNames.Contains(entityName))
                {
                    Report(
                        DiagnosticCodes.UnknownWidgetEntity,
                        $"The entity '{entityName}' was not found. No loaded entity has that name.",
                        $"{path}/entity");
                }
            }
            else if (findDataSource(widget.DataSource!) is { } dataSource)
            {
                recordEntity = dataSource.Entity;
                grouped = dataSource.Aggregate is not null;

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
            else if (grouped)
            {
                Report(
                    DiagnosticCodes.InvalidFormPage,
                    $"The data source '{widget.DataSource}' is grouped. A group is not a record, so the table cannot name a form page.",
                    $"{path}/formPage");
            }
            else if (findPage(formPage) is { } target)
            {
                // Rows of a data source carry the id of their root record, so the form must be over
                // that root entity. A form widget that names a form is over the form's entity, and a
                // form that is unknown is already reported on the target page.
                var targetWidget = target.Widgets[0];
                var targetEntity = targetWidget.Entity ?? (targetWidget.Form is { } targetForm ? findForm(targetForm)?.Entity : null);
                if (WidgetTypes.Parse(targetWidget.Type) != WidgetType.Form
                    || (recordEntity is not null && targetEntity is not null && !string.Equals(targetEntity, recordEntity, StringComparison.OrdinalIgnoreCase)))
                {
                    var expected = recordEntity is null ? "a form widget" : $"a form widget over '{recordEntity}'";
                    Report(
                        DiagnosticCodes.InvalidFormPage,
                        $"The page '{target.Name}' must hold {expected}, but holds a {targetWidget.Type} widget over '{targetEntity ?? targetWidget.DataSource}'.",
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
