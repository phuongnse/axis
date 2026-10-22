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
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, page.File, path, page.Id));

        ApplicationCompiler.CheckTextKey(action.Label, page, $"{actionPath}/label", references, diagnostics);

        // The schema requires the page of a navigate action.
        var pageName = action.Page!;
        if (findPage(pageName) is not { } target)
        {
            // A page that failed to load has its own diagnostics.
            if (!loaded.UnloadedPageNames.Contains(pageName))
            {
                Report(
                    DiagnosticCodes.UnknownNavigatePage,
                    $"The page '{pageName}' was not found. No loaded page has that name.",
                    $"{actionPath}/page");
            }

            return;
        }

        references.Add(page, $"{actionPath}/page", ReferenceRelations.NavigatePage, ResourceNode.Of(target));

        var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in action.Parameters ?? new Dictionary<string, string>())
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

            references.RecordExpression(page, path, type, problem);
            if (problem is not null)
            {
                Report(problem.Code, problem.Message, path);
            }
        }

        foreach (var parameter in target.Parameters.Where(parameter => parameter.Required == true && !given.ContainsKey(parameter.Name)))
        {
            Report(
                DiagnosticCodes.MissingNavigateParameter,
                $"The parameter '{parameter.Name}' is required by the page '{target.Name}', but the action does not give it.",
                actionPath);
        }
    }

    /// <summary>The type a page parameter holds, built as the page check builds it, or null for a type with no value, which has a diagnostic on the target page.</summary>
    private static ExpressionType? ExpectedType(PageParameterDefinition parameter) =>
        ExpressionScopes.TypeOf(new FieldDefinition
        {
            Name = parameter.Name,
            Type = parameter.Type,
            Values = parameter.Values,
            Target = parameter.Target,
        });
}
