using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the forms, sites and pages of an application against its entities, pages, data sources,
/// forms, processes and texts. A name that belongs to a file which was not loaded because of its own errors is
/// not reported again.
/// </summary>
internal static class PresentationChecker
{
    /// <summary>The most levels of widget lists on a page. The page's own list is level 1.</summary>
    private const int MaxWidgetDepth = 4;

    /// <summary>What the column widths of one <c>columns</c> widget add up to.</summary>
    private const int ColumnWidthTotal = 12;

    public static void Check(
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, FormResource?> findForm,
        Func<string, ProcessResource?> findProcess,
        Action<FieldDefinition, string, Action<string, string, string>> checkParameterField,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        IReadOnlyList<ExpressionRule> rules,
        IReadOnlySet<string> declaredModules,
        List<Diagnostic> diagnostics)
    {
        foreach (var form in loaded.Forms)
        {
            var isModule = ApplicationCompiler.IsModuleResource(form);
            CheckForm(
                form,
                loaded,
                findEntity,
                moduleReferences,
                references,
                isModule ? [] : rules,
                isModule ? null : declaredModules,
                diagnostics);
        }

        foreach (var page in loaded.Pages)
        {
            CheckPage(page, loaded, findEntity, findPage, findDataSource, findForm, findProcess, checkParameterField, moduleReferences, references, diagnostics);
        }

        // Locales are compared ignoring letter case, as the text check does.
        var textLocales = new HashSet<string>(loaded.Texts.Select(text => text.Locale), StringComparer.OrdinalIgnoreCase);
        var firstFileByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var site in loaded.Sites)
        {
            CheckSite(site, loaded, findPage, references, textLocales, firstFileByPath, diagnostics);
        }
    }

    /// <summary>
    /// Checks a form: its entity exists, every section title has a text key, and each field is a
    /// field of the entity listed once across all sections, ignoring letter case. Each condition
    /// parses and type-checks as a boolean over the entity, and reads only fields the form lists.
    /// A field the entity marks <c>required</c> has no <c>visibleWhen</c>. Section names are unique
    /// and, when the form has steps, every section is in exactly one step. A module form's
    /// conditions call no rules, as a module's data source filters do.
    /// </summary>
    private static void CheckForm(
        FormResource form,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        IReadOnlyList<ExpressionRule> rules,
        IReadOnlySet<string>? modules,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, form.File, path, form.Id));

        var entity = findEntity(form.Entity);
        if (entity is not null)
        {
            references.Add(form, "/entity", ReferenceRelations.FormEntity, ResourceNode.Of(entity));
        }

        if (entity is null
            && !loaded.UnloadedEntityNames.Contains(form.Entity)
            && !moduleReferences.ReportUndeclared(form.Entity, "/entity", Report))
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
            ApplicationCompiler.CheckTextKey(section.Title, form, $"/sections/{sectionIndex}/title", references, diagnostics);

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

        CheckSectionNames(form, Report);
        if (entity is not null)
        {
            CheckConditions(form, entity, findEntity, references, rules, modules, Report);
        }

        CheckSteps(form, references, diagnostics, Report);
    }

    /// <summary>Checks that section names are unique, ignoring letter case, and reports a repeat at its name.</summary>
    private static void CheckSectionNames(FormResource form, Action<string, string, string> report)
    {
        var firstPathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < form.Sections.Count; index++)
        {
            var name = form.Sections[index].Name;
            var path = $"/sections/{index}/name";
            if (name is not null && !firstPathByName.TryAdd(name, path))
            {
                report(
                    DiagnosticCodes.DuplicateFormSectionName,
                    $"The section name '{name}' is already used at '{firstPathByName[name]}'. A form names each section once.",
                    path);
            }
        }
    }

    /// <summary>
    /// Checks the steps: each title is a known text key, each step section names a section of the
    /// form, no section is in two steps, and, when there are steps, each section has a name and is in
    /// one. A section name matches ignoring letter case.
    /// </summary>
    private static void CheckSteps(
        FormResource form,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics,
        Action<string, string, string> report)
    {
        if (form.Steps is null)
        {
            return;
        }

        var sectionNames = new HashSet<string>(
            form.Sections.Select(section => section.Name).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        var firstPathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var stepIndex = 0; stepIndex < form.Steps.Count; stepIndex++)
        {
            var step = form.Steps[stepIndex];
            ApplicationCompiler.CheckTextKey(step.Title, form, $"/steps/{stepIndex}/title", references, diagnostics);

            for (var sectionIndex = 0; sectionIndex < step.Sections.Count; sectionIndex++)
            {
                var name = step.Sections[sectionIndex];
                var path = $"/steps/{stepIndex}/sections/{sectionIndex}";
                if (!sectionNames.Contains(name))
                {
                    report(
                        DiagnosticCodes.UnknownStepSection,
                        $"The step section '{name}' was not found. The form has no section with that name.",
                        path);
                }
                else if (!firstPathByName.TryAdd(name, path))
                {
                    report(
                        DiagnosticCodes.DuplicateStepSection,
                        $"The section '{name}' is already in a step at '{firstPathByName[name]}'. A section is in exactly one step.",
                        path);
                }
            }
        }

        for (var index = 0; index < form.Sections.Count; index++)
        {
            var name = form.Sections[index].Name;
            if (name is null)
            {
                report(
                    DiagnosticCodes.SectionInNoStep,
                    "The section has no name. When a form has steps, each section needs a name to be in a step.",
                    $"/sections/{index}");
            }
            else if (!firstPathByName.ContainsKey(name))
            {
                report(
                    DiagnosticCodes.SectionInNoStep,
                    $"The section '{name}' is in no step. When a form has steps, each section is in exactly one step.",
                    $"/sections/{index}");
            }
        }
    }

    /// <summary>
    /// Checks every condition of the form over the entity's fields, child collections and the named
    /// rules, and that <c>visibleWhen</c> is not on a field the entity marks <c>required</c>. A
    /// condition may name only fields the form lists. An expression problem is reported at the
    /// condition with its own code.
    /// </summary>
    private static void CheckConditions(
        FormResource form,
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        ReferenceIndexBuilder references,
        IReadOnlyList<ExpressionRule> rules,
        IReadOnlySet<string>? modules,
        Action<string, string, string> report)
    {
        var hasConditions = form.Sections.Any(section =>
            section.VisibleWhen is not null
            || section.Fields.Any(field => field.VisibleWhen is not null || field.RequiredWhen is not null || field.ReadOnlyWhen is not null));
        if (!hasConditions)
        {
            return;
        }

        var scope = ExpressionScopes.ForEntity(entity.Fields, rules: rules, findEntity: findEntity, modules: modules);
        var listed = new HashSet<string>(
            form.Sections.SelectMany(section => section.Fields).Select(field => field.Field), StringComparer.OrdinalIgnoreCase);

        void Check(string? expression, string path)
        {
            if (expression is not null)
            {
                CheckCondition(form, expression, path, scope, listed, modules is not null, references, report);
            }
        }

        for (var sectionIndex = 0; sectionIndex < form.Sections.Count; sectionIndex++)
        {
            var section = form.Sections[sectionIndex];
            Check(section.VisibleWhen, $"/sections/{sectionIndex}/visibleWhen");
            for (var fieldIndex = 0; fieldIndex < section.Fields.Count; fieldIndex++)
            {
                var field = section.Fields[fieldIndex];
                var path = $"/sections/{sectionIndex}/fields/{fieldIndex}";
                Check(field.VisibleWhen, $"{path}/visibleWhen");
                Check(field.RequiredWhen, $"{path}/requiredWhen");
                Check(field.ReadOnlyWhen, $"{path}/readOnlyWhen");

                if (field.VisibleWhen is not null && ApplicationCompiler.FindField(entity, field.Field)?.Required == true)
                {
                    report(
                        DiagnosticCodes.VisibleWhenOnRequiredField,
                        $"The field '{field.Field}' is required by the entity '{entity.Name}', so it cannot have 'visibleWhen'. The entity would still require a field nobody can fill.",
                        $"{path}/visibleWhen");
                }
            }
        }
    }

    private static void CheckCondition(
        FormResource form,
        string expression,
        string path,
        ExpressionScope scope,
        IReadOnlySet<string> listed,
        bool recordRuleCalls,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var parsed = ExpressionParser.Parse(expression);
        ExpressionDiagnostic? problem = parsed.Succeeded ? null : parsed.Diagnostic;
        ExpressionType? type = null;
        string? unlisted = null;
        if (parsed.Succeeded)
        {
            var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean);
            if (check.Succeeded)
            {
                // Module rule calls add no edges, as for module rules.
                if (recordRuleCalls)
                {
                    references.AddRuleCalls(form, path, check);
                }

                unlisted = FirstUnlistedField(parsed.Expression, check, listed);
            }

            problem = check.Diagnostic;
            type = check.Type;
        }

        references.RecordExpression(form, path, type, problem);

        if (problem is not null)
        {
            report(problem.Code, problem.Message, path);
        }
        else if (unlisted is not null)
        {
            report(
                DiagnosticCodes.UnlistedFormConditionField,
                $"The condition reads the field '{unlisted}', which the form does not list. A condition reads only fields the form lists.",
                path);
        }
    }

    /// <summary>
    /// The first field of the entity that <paramref name="node"/> reads and <paramref name="listed"/>
    /// lacks, or null. A path reads its first field. An aggregate reads its collection field, and
    /// its item expression reads the child row, so it is skipped. A rule body sees only its
    /// arguments, so a rule call reads what its arguments read.
    /// </summary>
    private static string? FirstUnlistedField(ExpressionNode node, ExpressionCheckResult check, IReadOnlySet<string> listed)
    {
        switch (node)
        {
            case NameNode name:
                return listed.Contains(name.Name) ? null : name.Name;

            case MemberNode member:
                return FirstUnlistedField(member.Target, check, listed);

            case UnaryNode unary:
                return FirstUnlistedField(unary.Operand, check, listed);

            case BinaryNode binary:
                return FirstUnlistedField(binary.Left, check, listed) ?? FirstUnlistedField(binary.Right, check, listed);

            case IsNullNode isNull:
                return FirstUnlistedField(isNull.Operand, check, listed);

            case InNode inList:
                return FirstUnlistedField(inList.Operand, check, listed)
                    ?? inList.Items.Select(item => FirstUnlistedField(item, check, listed)).FirstOrDefault(name => name is not null);

            case CallNode call:
                var isAggregate = !check.RuleCalls.ContainsKey(call)
                    && ExpressionFunctions.AggregateNames.Contains(call.Name, StringComparer.OrdinalIgnoreCase);
                var arguments = isAggregate ? call.Arguments.Take(1) : call.Arguments;
                return arguments.Select(argument => FirstUnlistedField(argument, check, listed)).FirstOrDefault(name => name is not null);

            default:
                return null;
        }
    }

    private static void CheckPage(
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, FormResource?> findForm,
        Func<string, ProcessResource?> findProcess,
        Action<FieldDefinition, string, Action<string, string, string>> checkParameterField,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, path, page.Id));

        ApplicationCompiler.CheckTextKey(page.Title, page, "/title", references, diagnostics);

        var parameterNames = new HashSet<string>(["page", "pageSize", "sort"], StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < page.Parameters.Count; index++)
        {
            var parameter = page.Parameters[index];
            var path = $"/parameters/{index}";
            if (!parameterNames.Add(parameter.Name))
            {
                Report(
                    DiagnosticCodes.InvalidPageParameterName,
                    $"The parameter name '{parameter.Name}' is reserved or already used by an earlier parameter. 'page', 'pageSize' and 'sort' are reserved, ignoring letter case, because they share the URL query with the table.",
                    $"{path}/name");
            }

            var definition = new FieldDefinition
            {
                Name = parameter.Name,
                Type = parameter.Type,
                Required = parameter.Required,
                Values = parameter.Values,
                Target = parameter.Target,
            };
            checkParameterField(definition, path, Report);
            ApplicationCompiler.AddFieldTarget(page, definition, path, findEntity, references);
        }

        var leaves = new List<(WidgetDefinition Widget, string Path)>();
        CheckWidgets(page.Widgets, "/widgets", 1, leaves, page, references, diagnostics);

        // A form page opens a record, which a navigate action names with 'record'.
        if (page.Parameters.Count > 0 && leaves.Any(leaf => leaf.Widget.Type == "form"))
        {
            Report(
                DiagnosticCodes.ParametersOnFormPage,
                "A page whose widget is a form declares no parameters. A navigate action opens its record with 'record'.",
                "/parameters");
        }

        foreach (var (widget, path) in leaves)
        {
            CheckLeaf(widget, path, page, loaded, findEntity, findPage, findDataSource, findForm, findProcess, moduleReferences, references, diagnostics);
        }

        // The URL of a page has one table state and one record part, so a second of either is ambiguous.
        var firstTable = leaves.FirstOrDefault(leaf => leaf.Widget.Type == "table").Path;
        var firstRecordWidget = leaves.FirstOrDefault(leaf => IsRecordWidget(leaf.Widget)).Path;
        foreach (var (widget, path) in leaves)
        {
            if (widget.Type == "table" && path != firstTable)
            {
                Report(
                    DiagnosticCodes.DuplicatePageTable,
                    $"The page already holds a table at '{firstTable}'. A page holds at most one table.",
                    path);
            }
            else if (IsRecordWidget(widget) && path != firstRecordWidget)
            {
                Report(
                    DiagnosticCodes.DuplicatePageRecordWidget,
                    $"The page already holds a {leaves.First(leaf => leaf.Path == firstRecordWidget).Widget.Type} widget at '{firstRecordWidget}'. A page holds at most one form, task inbox or process inspector widget.",
                    path);
            }
        }
    }

    /// <summary>Whether the widget owns the record part of the page's URL.</summary>
    private static bool IsRecordWidget(WidgetDefinition widget) =>
        widget.Type is "form" or "taskInbox" or "processInspector";

    /// <summary>
    /// Walks a list of widgets. Each container is checked for its depth and column widths, its text keys are
    /// checked, and its lists are walked one level deeper. Every other widget is collected in file order with
    /// its pointer, to be checked by <see cref="CheckLeaf"/>.
    /// </summary>
    private static void CheckWidgets(
        IReadOnlyList<WidgetDefinition> widgets,
        string listPath,
        int level,
        List<(WidgetDefinition Widget, string Path)> leaves,
        PageResource page,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        for (var index = 0; index < widgets.Count; index++)
        {
            var widget = widgets[index];
            var path = $"{listPath}/{index}";
            if (WidgetTypes.Parse(widget.Type) is not (WidgetType.Columns or WidgetType.Tabs or WidgetType.Section))
            {
                leaves.Add((widget, path));
                continue;
            }

            // A container on the last level would hold a list on a level beyond it.
            if (level >= MaxWidgetDepth)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.TooDeepWidgetTree,
                    $"The widget lists nest too deep. A page holds at most {MaxWidgetDepth} levels of widget lists, and this {widget.Type} widget would hold a list on level {level + 1}.",
                    page.File,
                    path,
                    page.Id));
                continue;
            }

            var columns = widget.Columns ?? [];
            if (columns.Count > 0 && columns.Sum(column => column.Width) != ColumnWidthTotal)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidColumnWidths,
                    $"The column widths add up to {columns.Sum(column => column.Width)}. The widths of one columns widget must add up to {ColumnWidthTotal}.",
                    page.File,
                    $"{path}/columns",
                    page.Id));
            }

            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                CheckWidgets(columns[columnIndex].Widgets, $"{path}/columns/{columnIndex}/widgets", level + 1, leaves, page, references, diagnostics);
            }

            var tabs = widget.Tabs ?? [];
            for (var tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
            {
                ApplicationCompiler.CheckTextKey(tabs[tabIndex].Label, page, $"{path}/tabs/{tabIndex}/label", references, diagnostics);
                CheckWidgets(tabs[tabIndex].Widgets, $"{path}/tabs/{tabIndex}/widgets", level + 1, leaves, page, references, diagnostics);
            }

            if (widget.Widgets is { } children)
            {
                ApplicationCompiler.CheckTextKey(widget.Title!, page, $"{path}/title", references, diagnostics);
                CheckWidgets(children, $"{path}/widgets", level + 1, leaves, page, references, diagnostics);
            }
        }
    }

    /// <summary>Checks one widget that shows something: its binding and every reference it holds.</summary>
    private static void CheckLeaf(
        WidgetDefinition widget,
        string path,
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, FormResource?> findForm,
        Func<string, ProcessResource?> findProcess,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, path, page.Id));

        var type = WidgetTypes.Parse(widget.Type);
        // A detail shows one record as a form widget does, so it takes the form widget's path.
        var isRecordLike = type is WidgetType.Form or WidgetType.Detail;

        if (type == WidgetType.TaskInbox)
        {
            // An inbox lists the signed-in user's tasks, so it binds no records and opens no form page.
            foreach (var (property, value) in new[] { ("entity", widget.Entity), ("dataSource", widget.DataSource), ("form", widget.Form) })
            {
                if (value is not null)
                {
                    Report(DiagnosticCodes.InvalidWidgetBinding, $"'{property}' does not apply to task inbox widgets.", $"{path}/{property}");
                }
            }

            if (widget.Actions is not null)
            {
                Report(DiagnosticCodes.InvalidWidgetBinding, "'actions' applies only to form and detail widgets.", $"{path}/actions");
            }

            ReportTableActions(widget, path, Report);
            ReportParameters(widget, path, "task inbox", Report);

            return;
        }

        if (type == WidgetType.ProcessInspector)
        {
            // An inspector lists the instances of one process, so it binds no records and opens no form page.
            foreach (var (property, value) in new[] { ("entity", widget.Entity), ("dataSource", widget.DataSource), ("form", widget.Form) })
            {
                if (value is not null)
                {
                    Report(DiagnosticCodes.InvalidWidgetBinding, $"'{property}' does not apply to process inspector widgets.", $"{path}/{property}");
                }
            }

            if (widget.Actions is not null)
            {
                Report(DiagnosticCodes.InvalidWidgetBinding, "'actions' applies only to form and detail widgets.", $"{path}/actions");
            }

            ReportTableActions(widget, path, Report);
            ReportParameters(widget, path, "process inspector", Report);

            // The page schema requires the process on an inspector.
            var processName = widget.Process!;
            if (findProcess(processName) is { } process)
            {
                references.Add(page, $"{path}/process", ReferenceRelations.WidgetProcess, ResourceNode.Of(process));
            }
            else if (!loaded.UnloadedProcessNames.Contains(processName))
            {
                Report(
                    DiagnosticCodes.UnknownWidgetProcess,
                    $"The process '{processName}' was not found. No loaded process has that name.",
                    $"{path}/process");
            }

            return;
        }

        if (type == WidgetType.Kpi)
        {
            CheckKpi(widget, path, page, loaded, findPage, findDataSource, findForm, moduleReferences, references, diagnostics);
            return;
        }

        if (type == WidgetType.Chart)
        {
            CheckChart(widget, path, page, loaded, findEntity, findDataSource, moduleReferences, references, diagnostics);
            return;
        }

        if (!isRecordLike && widget.Actions is not null)
        {
            // An action starts a process for one record, which only a form or a detail shows.
            Report(DiagnosticCodes.InvalidWidgetBinding, "'actions' applies only to form and detail widgets.", $"{path}/actions");
        }

        if (isRecordLike)
        {
            // A form or detail has one record and no rows or toolbar.
            ReportTableActions(widget, path, Report);
            ReportParameters(widget, path, widget.Type == "form" ? "form" : "detail", Report);
        }

        if (isRecordLike && widget.DataSource is not null)
        {
            // A form or detail shows one record of an entity, which a projection of rows cannot.
            Report(DiagnosticCodes.InvalidWidgetBinding, "'dataSource' applies only to table widgets.", $"{path}/dataSource");
            return;
        }

        if (isRecordLike && (widget.Form is null) == (widget.Entity is null))
        {
            // A form lays out the fields of its own entity, so naming an entity as well says nothing new.
            Report(DiagnosticCodes.InvalidWidgetBinding, $"A {widget.Type} widget must name exactly one of 'form' and 'entity'.", path);
            return;
        }

        if (!isRecordLike && widget.Form is not null)
        {
            Report(DiagnosticCodes.InvalidWidgetBinding, "'form' applies only to form and detail widgets.", $"{path}/form");
            return;
        }

        // A table shows all records of an entity or the rows of a data source, never both.
        if (!isRecordLike && (widget.Entity is null) == (widget.DataSource is null))
        {
            Report(
                DiagnosticCodes.InvalidWidgetBinding,
                "A widget must name exactly one of 'entity' and 'dataSource'.",
                path);
            return;
        }

        // The entity the widget's records belong to: its own, that of its form, or the root
        // entity of its data source. Null when the form or data source is unknown.
        string? recordEntity = widget.Entity;

        // Whether the widget shows the groups of a data source rather than records.
        var grouped = false;
        DataSourceResource? source = null;
        if (widget.Form is { } formName)
        {
            if (findForm(formName) is { } form)
            {
                references.Add(page, $"{path}/form", ReferenceRelations.WidgetForm, ResourceNode.Of(form));
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
            if (!isRecordLike)
            {
                // An entity has no parameters, so there is nothing to bind.
                ReportParameters(widget, path, "table over an entity", Report);
            }

            if (findEntity(entityName) is { } entity)
            {
                references.Add(page, $"{path}/entity", ReferenceRelations.WidgetEntity, ResourceNode.Of(entity));
            }
            else if (!loaded.UnloadedEntityNames.Contains(entityName))
            {
                Report(
                    DiagnosticCodes.UnknownWidgetEntity,
                    $"The entity '{entityName}' was not found. No loaded entity has that name.",
                    $"{path}/entity");
            }
        }
        else if (findDataSource(widget.DataSource!) is { } dataSource)
        {
            references.Add(page, $"{path}/dataSource", ReferenceRelations.WidgetDataSource, ResourceNode.Of(dataSource));
            recordEntity = dataSource.Entity;
            grouped = dataSource.Aggregate is not null;
            source = dataSource;

            CheckParameterBinding(widget, path, page, dataSource, Report);
        }
        else if (!loaded.UnloadedDataSourceNames.Contains(widget.DataSource!)
            && !moduleReferences.ReportUndeclared(widget.DataSource!, $"{path}/dataSource", Report))
        {
            Report(
                DiagnosticCodes.UnknownWidgetDataSource,
                $"The data source '{widget.DataSource}' was not found. No loaded data source has that name.",
                $"{path}/dataSource");
        }

        // What a navigate action's parameter sees on a row or record: the root entity's fields, or
        // the columns of the data source, plus the id. It is null when the entity or data source is
        // unknown, which has a diagnostic of its own. A navigate action over an entity resolves paths
        // through reference fields. visibleWhen and the columns of a data source do not.
        var rootEntity = recordEntity is null ? null : findEntity(recordEntity);
        var rowScope = NavigateScope(rootEntity, source, findEntity);
        var navigateScope = NavigateScope(rootEntity, source, findEntity, resolvePaths: true);

        if (isRecordLike)
        {
            CheckActions(widget, page, path, recordEntity, navigateScope, loaded, findPage, findForm, findProcess, references, diagnostics);
            if (type == WidgetType.Detail)
            {
                CheckDetailRecord(widget, path, page, recordEntity, Report);
            }
        }
        else
        {
            if (grouped && widget.RowActions is not null)
            {
                Report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    $"The data source '{widget.DataSource}' is grouped. A group row has no actions, so the table cannot name 'rowActions'.",
                    $"{path}/rowActions");
            }
            else
            {
                var rowActions = widget.RowActions ?? [];
                for (var index = 0; index < rowActions.Count; index++)
                {
                    var rowActionPath = $"{path}/rowActions/{index}";
                    if (rowActions[index].Type == "navigate")
                    {
                        NavigateChecker.Check(rowActions[index], rowActionPath, navigateScope, page, loaded, findPage, findForm, references, diagnostics);
                    }
                    else if (rowActions[index].Type == "delete")
                    {
                        ApplicationCompiler.CheckTextKey(rowActions[index].Label, page, $"{rowActionPath}/label", references, diagnostics);
                    }
                    else
                    {
                        CheckStartProcess(rowActions[index], rowActionPath, recordEntity, page, loaded, findProcess, references, diagnostics);
                    }

                    if (rowActions[index].VisibleWhen is { } visibleWhen)
                    {
                        CheckVisibleWhen(visibleWhen, $"{rowActionPath}/visibleWhen", rowScope, page, references, diagnostics);
                    }
                }
            }

            // A toolbar has no row, so its expressions see no names.
            var toolbarScope = ExpressionScopes.ForNavigate(null, []);
            var toolbarActions = widget.ToolbarActions ?? [];
            for (var index = 0; index < toolbarActions.Count; index++)
            {
                NavigateChecker.Check(toolbarActions[index], $"{path}/toolbarActions/{index}", toolbarScope, page, loaded, findPage, findForm, references, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks the <c>record</c> of a <c>detail</c> widget. It names a required <c>reference</c> parameter of the page
    /// whose target is the entity of the widget's records, ignoring letter case. The URL query then always holds the
    /// id of the record to show.
    /// </summary>
    private static void CheckDetailRecord(
        WidgetDefinition widget,
        string path,
        PageResource page,
        string? recordEntity,
        Action<string, string, string> report)
    {
        // The page schema requires the record of a detail.
        var name = widget.Record!;
        var recordPath = $"{path}/record";
        var parameter = page.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
        {
            report(DiagnosticCodes.InvalidDetailRecord, $"The record '{name}' is not a parameter of the page. Declare a required reference parameter with that name.", recordPath);
        }
        else if (parameter.Required != true)
        {
            report(DiagnosticCodes.InvalidDetailRecord, $"The parameter '{parameter.Name}' is optional, but a detail always needs a record. Make it required.", recordPath);
        }
        else if (parameter.Type != "reference")
        {
            report(DiagnosticCodes.InvalidDetailRecord, $"The parameter '{parameter.Name}' has type '{parameter.Type}', but a detail needs a reference to its record.", recordPath);
        }
        else if (recordEntity is not null && !string.Equals(parameter.Target, recordEntity, StringComparison.OrdinalIgnoreCase))
        {
            report(DiagnosticCodes.InvalidDetailRecord, $"The parameter '{parameter.Name}' refers to '{parameter.Target}', but the detail shows records of '{recordEntity}'.", recordPath);
        }
    }

    /// <summary>
    /// Checks a <c>kpi</c> widget. It shows one measure of the single total row of a data source, so it binds no
    /// entity or form and has no actions. The data source has an aggregate with no <c>groupBy</c>, each measure
    /// is one of the aggregate's, and no data source parameter is required, because a tile has no filter input.
    /// The expressions of its <c>navigate</c> action see only the page's parameters.
    /// </summary>
    private static void CheckKpi(
        WidgetDefinition widget,
        string path,
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, PageResource?> findPage,
        Func<string, DataSourceResource?> findDataSource,
        Func<string, FormResource?> findForm,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, path, page.Id));

        foreach (var (property, value) in new[] { ("entity", widget.Entity), ("form", widget.Form) })
        {
            if (value is not null)
            {
                Report(DiagnosticCodes.InvalidWidgetBinding, $"'{property}' does not apply to kpi widgets.", $"{path}/{property}");
            }
        }

        if (widget.Actions is not null)
        {
            Report(DiagnosticCodes.InvalidWidgetBinding, "'actions' applies only to form and detail widgets.", $"{path}/actions");
        }

        ReportTableActions(widget, path, Report);

        ApplicationCompiler.CheckTextKey(widget.Label!, page, $"{path}/label", references, diagnostics);
        if (widget.Comparison is { } comparison)
        {
            ApplicationCompiler.CheckTextKey(comparison.Label, page, $"{path}/comparison/label", references, diagnostics);
        }

        // The page schema requires the data source and the measure on a kpi.
        var dataSourceName = widget.DataSource!;
        if (findDataSource(dataSourceName) is { } dataSource)
        {
            references.Add(page, $"{path}/dataSource", ReferenceRelations.WidgetDataSource, ResourceNode.Of(dataSource));
            CheckKpiDataSource(widget, path, dataSource, Report);
        }
        else if (!loaded.UnloadedDataSourceNames.Contains(dataSourceName)
            && !moduleReferences.ReportUndeclared(dataSourceName, $"{path}/dataSource", Report))
        {
            Report(
                DiagnosticCodes.UnknownWidgetDataSource,
                $"The data source '{dataSourceName}' was not found. No loaded data source has that name.",
                $"{path}/dataSource");
        }

        if (widget.Navigate is { } navigate)
        {
            var scope = ExpressionScopes.ForNavigate(null, page.Parameters.Select(parameter => (parameter.Name, NavigateChecker.ExpectedType(parameter))));
            NavigateChecker.Check(navigate, $"{path}/navigate", scope, page, loaded, findPage, findForm, references, diagnostics);
        }
    }

    /// <summary>Checks the data source of a <c>kpi</c> widget: one total row, the named measures and no required parameter.</summary>
    private static void CheckKpiDataSource(WidgetDefinition widget, string path, DataSourceResource dataSource, Action<string, string, string> report)
    {
        if (dataSource.Aggregate is not { GroupBy.Count: 0 } aggregate)
        {
            report(
                DiagnosticCodes.KpiDataSourceNotTotal,
                $"The data source '{dataSource.Name}' must have an 'aggregate' with an empty 'groupBy', so that it has one total row. A kpi shows that row.",
                $"{path}/dataSource");
        }
        else
        {
            void CheckMeasure(string measure, string measurePath)
            {
                if (!aggregate.Measures.Any(candidate => string.Equals(candidate.Name, measure, StringComparison.OrdinalIgnoreCase)))
                {
                    report(
                        DiagnosticCodes.UnknownKpiMeasure,
                        $"The measure '{measure}' was not found. The data source '{dataSource.Name}' has no measure with that name.",
                        measurePath);
                }
            }

            CheckMeasure(widget.Measure!, $"{path}/measure");
            if (widget.Comparison is { } comparison)
            {
                CheckMeasure(comparison.Measure, $"{path}/comparison/measure");
            }
        }

        // A kpi has no filter input and binds no page parameter, so a required parameter would fail every request.
        foreach (var parameter in dataSource.Parameters.Where(parameter => parameter.Required == true))
        {
            report(
                DiagnosticCodes.InvalidWidgetBinding,
                $"The data source '{dataSource.Name}' has the required parameter '{parameter.Name}'. A kpi has no filter input, so every parameter of its data source must be optional.",
                $"{path}/dataSource");
        }
    }

    /// <summary>
    /// Checks a <c>chart</c> widget. It draws the groups of a data source, so it binds no entity or form and has no
    /// actions. The data source has an aggregate grouped by exactly one field, the category is that field, each
    /// measure is a number of the aggregate, a <c>pie</c> has one measure and no data source parameter is required,
    /// because a chart has no filter input.
    /// </summary>
    private static void CheckChart(
        WidgetDefinition widget,
        string path,
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        Func<string, DataSourceResource?> findDataSource,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, path, page.Id));

        foreach (var (property, value) in new[] { ("entity", widget.Entity), ("form", widget.Form) })
        {
            if (value is not null)
            {
                Report(DiagnosticCodes.InvalidWidgetBinding, $"'{property}' does not apply to chart widgets.", $"{path}/{property}");
            }
        }

        if (widget.Actions is not null)
        {
            Report(DiagnosticCodes.InvalidWidgetBinding, "'actions' applies only to form and detail widgets.", $"{path}/actions");
        }

        ReportTableActions(widget, path, Report);

        // The page schema requires the data source, the kind, the category, the measures and the title on a chart.
        ApplicationCompiler.CheckTextKey(widget.Title!, page, $"{path}/title", references, diagnostics);
        for (var index = 0; index < widget.Measures!.Count; index++)
        {
            ApplicationCompiler.CheckTextKey(widget.Measures[index].Label, page, $"{path}/measures/{index}/label", references, diagnostics);
        }

        var dataSourceName = widget.DataSource!;
        if (findDataSource(dataSourceName) is { } dataSource)
        {
            references.Add(page, $"{path}/dataSource", ReferenceRelations.WidgetDataSource, ResourceNode.Of(dataSource));
            CheckChartDataSource(widget, path, dataSource, findEntity, Report);
        }
        else if (!loaded.UnloadedDataSourceNames.Contains(dataSourceName)
            && !moduleReferences.ReportUndeclared(dataSourceName, $"{path}/dataSource", Report))
        {
            Report(
                DiagnosticCodes.UnknownWidgetDataSource,
                $"The data source '{dataSourceName}' was not found. No loaded data source has that name.",
                $"{path}/dataSource");
        }
    }

    /// <summary>
    /// Checks the data source of a <c>chart</c> widget: one group field that is the category, numeric measures that the
    /// aggregate declares, one measure on a <c>pie</c> and no required parameter.
    /// </summary>
    private static void CheckChartDataSource(
        WidgetDefinition widget,
        string path,
        DataSourceResource dataSource,
        Func<string, EntityResource?> findEntity,
        Action<string, string, string> report)
    {
        if (dataSource.Aggregate is not { } aggregate)
        {
            report(
                DiagnosticCodes.ChartDataSourceNotGrouped,
                $"The data source '{dataSource.Name}' must have an 'aggregate' with exactly one 'groupBy' field. A chart draws its groups.",
                $"{path}/dataSource");
            return;
        }

        if (aggregate.GroupBy.Count != 1)
        {
            report(
                DiagnosticCodes.ChartDataSourceNotGrouped,
                $"The data source '{dataSource.Name}' must have an 'aggregate' with exactly one 'groupBy' field, not {aggregate.GroupBy.Count}. A chart draws its groups.",
                $"{path}/dataSource");
        }
        else if (!string.Equals(aggregate.GroupBy[0], widget.Category, StringComparison.OrdinalIgnoreCase))
        {
            report(
                DiagnosticCodes.ChartDataSourceNotGrouped,
                $"The category '{widget.Category}' must be the group field '{aggregate.GroupBy[0]}' of the data source '{dataSource.Name}'.",
                $"{path}/category");
        }

        var measures = widget.Measures!;
        for (var index = 0; index < measures.Count; index++)
        {
            var name = measures[index].Name;
            var measure = aggregate.Measures.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (measure is null)
            {
                report(
                    DiagnosticCodes.InvalidChartMeasure,
                    $"The measure '{name}' was not found. The data source '{dataSource.Name}' has no measure with that name.",
                    $"{path}/measures/{index}/name");
            }
            else if (!IsNumericMeasure(measure, dataSource, findEntity))
            {
                report(
                    DiagnosticCodes.InvalidChartMeasure,
                    $"The measure '{measure.Name}' is not a number. A chart draws a 'count' or a 'sum', or a 'min' or 'max' of an integer or a decimal.",
                    $"{path}/measures/{index}/name");
            }
        }

        if (string.Equals(widget.Kind, "pie", StringComparison.Ordinal) && measures.Count > 1)
        {
            report(
                DiagnosticCodes.InvalidChartMeasure,
                $"A pie chart draws one measure, not {measures.Count}.",
                $"{path}/measures");
        }

        // A chart has no filter input and binds no page parameter, so a required parameter would fail every request.
        foreach (var parameter in dataSource.Parameters.Where(parameter => parameter.Required == true))
        {
            report(
                DiagnosticCodes.InvalidWidgetBinding,
                $"The data source '{dataSource.Name}' has the required parameter '{parameter.Name}'. A chart has no filter input, so every parameter of its data source must be optional.",
                $"{path}/dataSource");
        }
    }

    /// <summary>
    /// Whether a measure is a number. A <c>count</c> and a <c>sum</c> are. A <c>min</c> or <c>max</c> keeps the type of
    /// its field, so it is a number only over an integer or a decimal. A field whose path does not resolve is not
    /// reported again.
    /// </summary>
    private static bool IsNumericMeasure(DataSourceMeasureDefinition measure, DataSourceResource dataSource, Func<string, EntityResource?> findEntity)
    {
        if (measure.Function is "count" or "sum")
        {
            return true;
        }

        var field = dataSource.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, measure.Field, StringComparison.Ordinal));
        if (field is null || findEntity(dataSource.Entity) is not { } root
            || ApplicationCompiler.ResolvePath(root, field.Path, findEntity, _ => { }, out var hops) is not null
            || hops.Count == 0)
        {
            return true;
        }

        return FieldTypes.Parse(hops[^1].Type) is FieldType.Integer or FieldType.Decimal;
    }

    /// <summary>Reports <c>parameters</c> on a widget that has no data source parameter to bind.</summary>
    private static void ReportParameters(WidgetDefinition widget, string path, string kind, Action<string, string, string> report)
    {
        if (widget.Parameters is not null)
        {
            report(DiagnosticCodes.InvalidWidgetBinding, $"'parameters' applies only to table widgets over a data source, not to a {kind}.", $"{path}/parameters");
        }
    }

    /// <summary>
    /// Checks how a table gives the parameters of its data source their values from the parameters of its page. Each
    /// entry names a parameter of each, with the same type. A required parameter is bound to a required page
    /// parameter. A page parameter does not share its name with a parameter the table leaves unbound, because both
    /// are in the URL query as filter inputs.
    /// </summary>
    private static void CheckParameterBinding(
        WidgetDefinition widget,
        string path,
        PageResource page,
        DataSourceResource dataSource,
        Action<string, string, string> report)
    {
        var binding = widget.Parameters ?? new Dictionary<string, string>();
        var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in binding)
        {
            var entryPath = $"{path}/parameters/{key}";
            var parameter = dataSource.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    $"The data source '{dataSource.Name}' has no parameter '{key}'.",
                    entryPath);
            }
            else if (!bound.Add(parameter.Name))
            {
                report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    $"The parameter '{parameter.Name}' of the data source '{dataSource.Name}' is bound more than once, ignoring letter case.",
                    entryPath);
            }

            var pageParameter = page.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase));
            if (pageParameter is null)
            {
                report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    $"The page '{page.Name}' has no parameter '{value}'.",
                    entryPath);
            }

            if (parameter is null || pageParameter is null)
            {
                continue;
            }

            if (!SameParameterType(pageParameter, parameter))
            {
                report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    $"The page parameter '{pageParameter.Name}' cannot give the data source parameter '{parameter.Name}' its value. Their types differ, and an enum page parameter must list only values of the data source parameter.",
                    entryPath);
            }

            if (parameter.Required == true && pageParameter.Required != true)
            {
                report(
                    DiagnosticCodes.InvalidWidgetBinding,
                    $"The data source parameter '{parameter.Name}' is required, so the page parameter '{pageParameter.Name}' that gives its value must be required too.",
                    entryPath);
            }
        }

        // A table has an input for each parameter it leaves unbound, so a required one would make every request
        // for its rows fail unless a page parameter gives it a value.
        foreach (var parameter in dataSource.Parameters.Where(parameter => parameter.Required == true && !bound.Contains(parameter.Name)))
        {
            report(
                DiagnosticCodes.InvalidWidgetBinding,
                $"The data source '{dataSource.Name}' has the required parameter '{parameter.Name}'. A table can only show it when the parameter is bound to a required page parameter with 'parameters'.",
                $"{path}/dataSource");
        }

        var unbound = new HashSet<string>(
            dataSource.Parameters.Select(parameter => parameter.Name).Where(name => !bound.Contains(name)),
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < page.Parameters.Count; index++)
        {
            if (unbound.Contains(page.Parameters[index].Name))
            {
                report(
                    DiagnosticCodes.InvalidPageParameterName,
                    $"The parameter name '{page.Parameters[index].Name}' is used by a parameter of the data source '{dataSource.Name}' that its table leaves unbound. Both share the URL query, ignoring letter case. Bind the parameter or rename one of them.",
                    $"/parameters/{index}/name");
            }
        }
    }

    /// <summary>
    /// Whether every value the page parameter accepts is one the data source parameter accepts: equal types, the
    /// same target for a reference, and for an enum a subset of its values.
    /// </summary>
    private static bool SameParameterType(PageParameterDefinition pageParameter, DataSourceParameterDefinition parameter)
    {
        if (pageParameter.Type != parameter.Type)
        {
            return false;
        }

        return pageParameter.Type switch
        {
            "reference" => string.Equals(pageParameter.Target, parameter.Target, StringComparison.OrdinalIgnoreCase),
            "enum" => (pageParameter.Values ?? []).All(value => (parameter.Values ?? []).Contains(value)),
            _ => true,
        };
    }

    /// <summary>Reports <c>rowActions</c> and <c>toolbarActions</c> on a widget that is not a table.</summary>
    private static void ReportTableActions(WidgetDefinition widget, string path, Action<string, string, string> report)
    {
        foreach (var (property, value) in new[] { ("rowActions", widget.RowActions), ("toolbarActions", widget.ToolbarActions) })
        {
            if (value is not null)
            {
                report(DiagnosticCodes.InvalidWidgetBinding, $"'{property}' applies only to table widgets.", $"{path}/{property}");
            }
        }
    }

    /// <summary>
    /// The scope of a navigate action on a row or record: <c>id</c> and the fields of the root
    /// entity, or, for a data source, the projected columns whose path resolves. Null when the
    /// root entity is not known. Paths through reference fields resolve only when
    /// <paramref name="resolvePaths"/> is set.
    /// </summary>
    private static ExpressionScope? NavigateScope(
        EntityResource? root, DataSourceResource? source, Func<string, EntityResource?> findEntity, bool resolvePaths = false)
    {
        if (root is null)
        {
            return null;
        }

        if (source is null)
        {
            return ExpressionScopes.ForNavigate(
                root.Name,
                root.Fields.Select(field => (field.Name, ExpressionScopes.TypeOf(field))),
                resolvePaths ? findEntity : null);
        }

        // A path that does not resolve has a diagnostic on the data source. One that reaches an
        // entity that was not loaded has no type, so its column is not in the scope.
        var columns = new List<(string Name, ExpressionType? Type)>();
        foreach (var field in source.Fields)
        {
            if (ApplicationCompiler.ResolvePath(root, field.Path, findEntity, _ => { }, out var hops) is null && hops.Count > 0)
            {
                columns.Add((field.Name, ExpressionScopes.TypeOf(hops[^1])));
            }
        }

        return ExpressionScopes.ForNavigate(root.Name, columns);
    }

    /// <summary>The widgets that show something, not the containers around them, depth first in file order.</summary>
    internal static IEnumerable<WidgetDefinition> Leaves(IEnumerable<WidgetDefinition> widgets) =>
        widgets.SelectMany(widget => WidgetTypes.Parse(widget.Type) switch
        {
            WidgetType.Columns => Leaves(widget.Columns!.SelectMany(column => column.Widgets)),
            WidgetType.Tabs => Leaves(widget.Tabs!.SelectMany(tab => tab.Widgets)),
            WidgetType.Section => Leaves(widget.Widgets!),
            _ => [widget],
        });

    /// <summary>
    /// Checks the actions of a form widget: each label has a text key. A <c>startProcess</c> action
    /// is checked by <see cref="CheckStartProcess"/> against the entity of the form's records. A
    /// <c>navigate</c> action is checked by <see cref="NavigateChecker"/> against
    /// <paramref name="scope"/>, the names of the form's record.
    /// </summary>
    private static void CheckActions(
        WidgetDefinition widget,
        PageResource page,
        string path,
        string? recordEntity,
        ExpressionScope? scope,
        ApplicationLoadResult loaded,
        Func<string, PageResource?> findPage,
        Func<string, FormResource?> findForm,
        Func<string, ProcessResource?> findProcess,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        var actions = widget.Actions ?? [];
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var actionPath = $"{path}/actions/{index}";
            if (action.Type == "navigate")
            {
                NavigateChecker.Check(action, actionPath, scope, page, loaded, findPage, findForm, references, diagnostics);
                continue;
            }

            CheckStartProcess(action, actionPath, recordEntity, page, loaded, findProcess, references, diagnostics);
        }
    }

    /// <summary>
    /// Checks a <c>startProcess</c> action of a form or a table row: its label has a text key and
    /// its process exists and is over <paramref name="recordEntity"/>, ignoring letter case.
    /// </summary>
    private static void CheckStartProcess(
        WidgetActionDefinition action,
        string actionPath,
        string? recordEntity,
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, ProcessResource?> findProcess,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        ApplicationCompiler.CheckTextKey(action.Label, page, $"{actionPath}/label", references, diagnostics);

        // The schema requires the process of a startProcess action.
        var processName = action.Process!;
        if (findProcess(processName) is not { } process)
        {
            if (!loaded.UnloadedProcessNames.Contains(processName))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownActionProcess,
                    $"The process '{processName}' was not found. No loaded process has that name.",
                    page.File,
                    $"{actionPath}/process",
                    page.Id));
            }
        }
        else
        {
            references.Add(page, $"{actionPath}/process", ReferenceRelations.ActionProcess, ResourceNode.Of(process));
            if (recordEntity is not null && !string.Equals(process.Entity, recordEntity, StringComparison.OrdinalIgnoreCase))
            {
                // The action starts the process for the record, so it must be its subject.
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ActionProcessOverOtherEntity,
                    $"The process '{process.Name}' is over the entity '{process.Entity}', but the widget shows records of '{recordEntity}'.",
                    page.File,
                    $"{actionPath}/process",
                    page.Id));
            }
        }
    }

    /// <summary>
    /// Checks the <c>visibleWhen</c> of a row action: a boolean expression over the row. When
    /// <paramref name="scope"/> is null, because the record or data source of the widget is unknown and
    /// has a diagnostic of its own, it is only parsed.
    /// </summary>
    private static void CheckVisibleWhen(
        string expression,
        string path,
        ExpressionScope? scope,
        PageResource page,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        var parsed = ExpressionParser.Parse(expression);
        ExpressionType? type = null;
        ExpressionDiagnostic? problem;
        if (!parsed.Succeeded)
        {
            problem = parsed.Diagnostic;
        }
        else if (scope is null)
        {
            problem = null;
        }
        else
        {
            var check = ExpressionTypeChecker.Check(parsed.Expression!, scope, ExpressionType.Boolean);
            type = check.Type;
            problem = check.Diagnostic;
        }

        references.RecordExpression(page, path, type, problem);
        if (problem is not null)
        {
            diagnostics.Add(new Diagnostic(problem.Code, problem.Message, page.File, path, page.Id));
        }
    }

    private static void CheckSite(
        SiteResource site,
        ApplicationLoadResult loaded,
        Func<string, PageResource?> findPage,
        ReferenceIndexBuilder references,
        HashSet<string> textLocales,
        Dictionary<string, string> firstFileByPath,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, site.File, path, site.Id));

        ApplicationCompiler.CheckTextKey(site.Title, site, "/title", references, diagnostics);

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
            ApplicationCompiler.CheckTextKey(entry.Label, site, $"{path}/label", references, diagnostics);

            if (findPage(entry.Page) is { } page)
            {
                references.Add(site, $"{path}/page", ReferenceRelations.NavigationPage, ResourceNode.Of(page));
            }
            else if (!loaded.UnloadedPageNames.Contains(entry.Page))
            {
                Report(
                    DiagnosticCodes.UnknownNavigationPage,
                    $"The page '{entry.Page}' was not found. No loaded page has that name.",
                    $"{path}/page");
            }
        }
    }
}
