using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks a <c>navigate</c> action of a page: its label has a text key, its target page exists, and
/// each parameter it gives is a parameter of that page whose expression fits the parameter's type.
/// Every required parameter of the target is given. Each target gets a <c>navigate page</c> edge.
/// </summary>
internal static class NavigateChecker
{
    /// <summary>
    /// Checks the action at <paramref name="actionPath"/> of <paramref name="page"/>. The expressions
    /// are checked against <paramref name="scope"/>. When it is null, because the record or data
    /// source of the widget is unknown and has a diagnostic of its own, they are only parsed.
    /// </summary>
    public static void Check(
        WidgetActionDefinition action,
        string actionPath,
        ExpressionScope? scope,
        PageResource page,
        ApplicationLoadResult loaded,
        Func<string, PageResource?> findPage,
        Func<string, FormResource?> findForm,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        ApplicationCompiler.CheckTextKey(action.Label, page, $"{actionPath}/label", references, diagnostics);

        // The schema requires the page of a navigate action.
        var target = CheckTarget(
            action.Page!, action.Parameters, actionPath, scope, page, loaded.UnloadedPageNames, findPage, references, diagnostics);
        if (target is not null && action.Record is not null)
        {
            CheckRecord(action.Record, actionPath, scope, target, page, findForm, references, diagnostics);
        }
    }

    /// <summary>
    /// Checks the page <paramref name="pageName"/> names and the <paramref name="parameters"/> given to
    /// it: the page exists, unless its file was not loaded, and gets a <c>navigate page</c> edge from
    /// <c>{actionPath}/page</c>. Each parameter is a parameter of the page, given once, and its
    /// expression fits the parameter's type over <paramref name="scope"/>, which may be null. Every required
    /// parameter is given. Diagnostics are reported on <paramref name="owner"/>. Gives the page, or null
    /// when it was not found. <paramref name="giver"/> names what gives the parameters in a message.
    /// </summary>
    internal static PageResource? CheckTarget(
        string pageName,
        IReadOnlyDictionary<string, string>? parameters,
        string actionPath,
        ExpressionScope? scope,
        Resource owner,
        IReadOnlySet<string> unloadedPageNames,
        Func<string, PageResource?> findPage,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics,
        string giver = "action")
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, owner.File, path, owner.Id));

        if (findPage(pageName) is not { } target)
        {
            // A page that failed to load has its own diagnostics.
            if (!unloadedPageNames.Contains(pageName))
            {
                Report(
                    DiagnosticCodes.UnknownNavigatePage,
                    $"The page '{pageName}' was not found. No loaded page has that name.",
                    $"{actionPath}/page");
            }

            return null;
        }

        references.Add(owner, $"{actionPath}/page", ReferenceRelations.NavigatePage, ResourceNode.Of(target));

        var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in parameters ?? new Dictionary<string, string>())
        {
            var path = $"{actionPath}/parameters/{key}";
            var parameter = target.Parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                Report(
                    DiagnosticCodes.UnknownNavigateParameter,
                    $"The parameter '{key}' was not found. The page '{target.Name}' has no parameter with that name.",
                    path);
                continue;
            }

            if (!given.TryAdd(parameter.Name, key))
            {
                Report(
                    DiagnosticCodes.UnknownNavigateParameter,
                    $"The parameter '{parameter.Name}' is already given by '{given[parameter.Name]}'.",
                    path);
                continue;
            }

            var parsed = ExpressionParser.Parse(expression);
            ExpressionType? type = null;
            ExpressionDiagnostic? problem;
            if (!parsed.Succeeded)
            {
                problem = parsed.Diagnostic;
            }
            else if (scope is null || ExpectedType(parameter) is not { } expected)
            {
                problem = null;
            }
            else
            {
                var check = ExpressionTypeChecker.Check(parsed.Expression!, scope, expected);
                type = check.Type;
                problem = check.Diagnostic;
            }

            references.RecordExpression(owner, path, type, problem);
            if (problem is not null)
            {
                Report(problem.Code, problem.Message, path);
            }
        }

        foreach (var parameter in target.Parameters.Where(parameter => parameter.Required == true && !given.ContainsKey(parameter.Name)))
        {
            Report(
                DiagnosticCodes.MissingNavigateParameter,
                $"The parameter '{parameter.Name}' is required by the page '{target.Name}', but the {giver} does not give it.",
                actionPath);
        }

        return target;
    }

    /// <summary>
    /// Checks the <c>record</c> of an action: the target page holds a form widget, and the
    /// expression is a reference to the entity of that form.
    /// </summary>
    private static void CheckRecord(
        string record,
        string actionPath,
        ExpressionScope? scope,
        PageResource target,
        PageResource page,
        Func<string, FormResource?> findForm,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, $"{actionPath}/record", page.Id));

        var formWidget = PresentationChecker.Leaves(target.Widgets).FirstOrDefault(leaf => leaf.Type == "form");
        if (formWidget is null)
        {
            Report(
                DiagnosticCodes.InvalidNavigateRecord,
                $"The page '{target.Name}' holds no form widget, so the action cannot set 'record'.");
            return;
        }

        // A form that is unknown is already reported on the target page.
        var entity = formWidget.Entity ?? (formWidget.Form is { } formName ? findForm(formName)?.Entity : null);
        var parsed = ExpressionParser.Parse(record);
        ExpressionType? type = null;
        ExpressionDiagnostic? problem;
        if (!parsed.Succeeded)
        {
            problem = parsed.Diagnostic;
        }
        else if (scope is null || entity is null)
        {
            problem = null;
        }
        else
        {
            var check = ExpressionTypeChecker.Check(parsed.Expression!, scope, ExpressionType.Reference(entity));
            type = check.Type;
            problem = check.Diagnostic;
        }

        references.RecordExpression(page, $"{actionPath}/record", type, problem);
        if (problem is null)
        {
            return;
        }

        if (problem.Code == ExpressionDiagnosticCodes.ResultTypeMismatch)
        {
            Report(
                DiagnosticCodes.InvalidNavigateRecord,
                $"The page '{target.Name}' opens a record of '{entity}', so 'record' must be a reference to it. {problem.Message}");
        }
        else
        {
            Report(problem.Code, problem.Message);
        }
    }

    /// <summary>The type a page parameter holds, built as the page check builds it, or null for a type with no value, which has a diagnostic on the target page.</summary>
    internal static ExpressionType? ExpectedType(PageParameterDefinition parameter) =>
        ExpressionScopes.TypeOf(new FieldDefinition
        {
            Name = parameter.Name,
            Type = parameter.Type,
            Values = parameter.Values,
            Target = parameter.Target,
        });
}
