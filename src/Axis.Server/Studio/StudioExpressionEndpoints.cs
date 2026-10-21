using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Typing;
using Json.Patch;
using Json.Pointer;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// <c>POST /api/studio/apps/{application}/expressions/check</c> checks one expression where it would stand. The body is
/// <c>{ resourceId, path, expression }</c>: the resource's id, the JSON Pointer of a string in it, and the text to try.
/// It puts the text into a copy of the saved resource and compiles the folder in memory, so the answer is the compile's
/// own and nothing is written or locked. It answers <c>{ type, diagnostics }</c>. A diagnostic has the compile's
/// <c>code</c> and <c>message</c> and a character range, <c>start</c> and <c>end</c>, as UTF-16 offsets into the
/// expression with <c>end</c> exclusive. <c>type</c> is null when there is a diagnostic. The checks run in the order of the
/// rest of the authoring API: session (401), author role (403), application (404), then 415 for a content type other than
/// <c>application/json</c>, 400 for a body that is not an expression check, 404 for a resource id the application does
/// not have, and 400 for a path that holds no expression. A path holds no expression when it does not point at a string,
/// or when the compile checks no expression there, as when another problem in the resource skips the check.
/// </summary>
internal static class StudioExpressionEndpoints
{
    public static void MapStudioExpressionEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/studio/apps/{application}/expressions/check", CheckAsync);

    private static async Task<IResult> CheckAsync(
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

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        JsonObject? body;
        try
        {
            body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            body = null;
        }

        if (body is null
            || body["resourceId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id)
            || id.Length != 36 || !Guid.TryParseExact(id, "D", out var resourceId)
            || body["path"] is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var path)
            || !JsonPointer.TryParse(path, out var pointer)
            || body["expression"] is not JsonValue expressionValue || !expressionValue.TryGetValue<string>(out var expression))
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not a valid expression check.");
        }

        var (resources, folderFiles) = StudioFolder.ListWithContent(folder);
        if (resources.FirstOrDefault(file => file.Id == resourceId) is not { } found)
        {
            return StudioEndpoints.NotFound(StudioEndpoints.NoResource);
        }

        var edited = Replace(found, pointer, expression);
        if (edited is null)
        {
            return NoExpression();
        }

        var files = folderFiles
            .Select(file => string.Equals(file.Path, found.File, StringComparison.Ordinal) ? new ResourceContent(file.Path, edited) : file)
            .ToList();
        var probe = new ExpressionProbe(found.File, path);
        var result = ApplicationCompiler.CompileFiles(files, packages.Versions, modules.Modules, probe);
        if (!probe.Found)
        {
            return NoExpression();
        }

        StudioExpressionDiagnostic? diagnostic = null;
        if (probe.Diagnostic is { } problem)
        {
            var (start, end) = ExpressionRange.Of(expression, problem);
            diagnostic = new StudioExpressionDiagnostic(problem.Code, problem.Message, start, end);
        }
        else if (result.Diagnostics.FirstOrDefault(
            item => string.Equals(item.File, found.File, StringComparison.Ordinal) && string.Equals(item.Path, path, StringComparison.Ordinal)) is { } other)
        {
            // A limit the compile reports at the place but not inside the expression check, such as a rule cycle.
            diagnostic = new StudioExpressionDiagnostic(other.Code, other.Message, 0, expression.Length);
        }

        return Results.Ok(new StudioExpressionCheckResponse(
            diagnostic is null ? TypeName(probe.Type) : null,
            diagnostic is null ? [] : [diagnostic]));
    }

    private static IResult NoExpression() =>
        StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The path holds no expression.");

    /// <summary>The resource's JSON with the string at <paramref name="pointer"/> replaced, or null when it holds no string there.</summary>
    private static string? Replace(StudioFile file, JsonPointer pointer, string expression)
    {
        try
        {
            var content = JsonNode.Parse(file.Bytes);
            if (!pointer.TryEvaluate(content, out var current) || current is not JsonValue value || !value.TryGetValue<string>(out _))
            {
                return null;
            }

            var result = new JsonPatch(PatchOperation.Replace(pointer, JsonValue.Create(expression))).Apply(content);
            return result.IsSuccess ? result.Result?.ToJsonString() : null;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // The names of the Types table in docs/reference/expressions.md.
    private static string? TypeName(ExpressionType? type) => type?.Kind switch
    {
        ExpressionTypeKind.Null => "null",
        ExpressionTypeKind.Text => "text",
        ExpressionTypeKind.Integer => "integer",
        ExpressionTypeKind.Decimal => "decimal",
        ExpressionTypeKind.Boolean => "boolean",
        ExpressionTypeKind.Date => "date",
        ExpressionTypeKind.DateTime => "date-time",
        ExpressionTypeKind.Enum => "enum",
        ExpressionTypeKind.Reference => "reference",
        _ => null,
    };
}

/// <summary>The answer to an expression check: the expression's type, and the problem with its range when there is one.</summary>
internal sealed record StudioExpressionCheckResponse(string? Type, IReadOnlyList<StudioExpressionDiagnostic> Diagnostics);

/// <summary>One problem of an expression. <c>Start</c> and <c>End</c> are UTF-16 offsets into the expression, and <c>End</c> is exclusive.</summary>
internal sealed record StudioExpressionDiagnostic(string Code, string Message, int Start, int End);
