using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Typing;
using Axis.Studio;
using Json.Patch;
using Json.Pointer;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// <c>POST /api/studio/apps/{application}/expressions/check</c> checks one expression where it would stand. The body is
/// <c>{ resourceId, path, expression, draft }</c>: the resource's id, the JSON Pointer of a string in it, the text to try,
/// and optionally the resource as the author has it. The path is evaluated in the draft when one is given, and in the
/// saved resource otherwise. The text goes into a copy of that resource and the folder compiles in memory, so the answer
/// is the compile's own and nothing is written or locked. It answers <c>{ type, diagnostics }</c>. A diagnostic has the
/// compile's <c>code</c> and <c>message</c> and a character range, <c>start</c> and <c>end</c>, as UTF-16 offsets into the
/// expression with <c>end</c> exclusive. <c>type</c> is null when there is a diagnostic.
/// <c>POST /api/studio/apps/{application}/expressions/scope</c> answers what the same place offers. Its body is
/// <c>{ resourceId, path, draft }</c> and the answer is <c>{ type, fields, functions }</c>, from the compile's type check at
/// the place. The checks run in the order of the rest of the authoring API for both routes: session (401), author role (403),
/// application (404), then 415 for a content type other than
/// <c>application/json</c>, 400 for a body that is not an expression check or a <c>draft</c> that is not an object, 404 for a
/// resource id the application does not have, and 400 for a path that holds no expression. A path holds no expression when
/// it points at neither a string nor a missing property of an object the resource has, so a condition not yet saved is
/// checked in place. It also holds none when the compile checks no expression there, as when another problem in the
/// resource skips the check.
/// </summary>
internal static class StudioExpressionEndpoints
{
    public static void MapStudioExpressionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/studio/apps/{application}/expressions/check", CheckAsync);
        endpoints.MapPost("/api/studio/apps/{application}/expressions/scope", ScopeAsync);
    }

    private static async Task<IResult> CheckAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (request, refusal) = await ReadAsync(application, httpContext, options, withExpression: true, BadCheck);
        if (request is null)
        {
            return refusal!;
        }

        var (answer, problem) = Check(
            request.Resources, request.Files, request.ResourceId, request.Path, request.Pointer, request.Expression!, packages, modules, request.Draft);
        return Answer(answer, problem);
    }

    private static async Task<IResult> ScopeAsync(
        string application,
        HttpContext httpContext,
        StudioOptions options,
        PackageCatalog packages,
        ModuleCatalog modules)
    {
        var (request, refusal) = await ReadAsync(application, httpContext, options, withExpression: false, BadScope);
        if (request is null)
        {
            return refusal!;
        }

        var (answer, problem) = Scope(
            request.Resources, request.Files, request.ResourceId, request.Path, request.Pointer, packages, modules, request.Draft);
        return Answer(answer, problem);
    }

    private static IResult Answer(object? answer, string? problem) => problem switch
    {
        null => Results.Ok(answer),
        StudioEndpoints.NoResource => StudioEndpoints.NotFound(StudioEndpoints.NoResource),
        _ => StudioEndpoints.Problem(StatusCodes.Status400BadRequest, problem),
    };

    /// <summary>The body both routes read. <c>Expression</c> is set only for the check.</summary>
    private sealed record ExpressionRequest(
        IReadOnlyList<StudioFile> Resources,
        IReadOnlyList<ResourceContent> Files,
        Guid ResourceId,
        string Path,
        JsonPointer Pointer,
        string? Expression,
        JsonObject? Draft);

    /// <summary>
    /// The refusals both routes share, in order: the application (401, 403, 404), the content type (415) and a body that is
    /// not a request (400 with <paramref name="badTitle"/>). Otherwise the folder's files and the body's parts.
    /// </summary>
    private static async Task<(ExpressionRequest? Request, IResult? Refusal)> ReadAsync(
        string application, HttpContext httpContext, StudioOptions options, bool withExpression, string badTitle)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return (null, refusal);
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return (null, StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json."));
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

        string? expression = null;
        if (body is null
            || body["resourceId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id)
            || id.Length != 36 || !Guid.TryParseExact(id, "D", out var resourceId)
            || body["path"] is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var path)
            || !JsonPointer.TryParse(path, out var pointer)
            || (withExpression && (body["expression"] is not JsonValue expressionValue || !expressionValue.TryGetValue(out expression)))
            || body["draft"] is not (null or JsonObject))
        {
            return (null, StudioEndpoints.Problem(StatusCodes.Status400BadRequest, badTitle));
        }

        var (resources, files) = StudioFolder.ListWithContent(folder);
        return (new ExpressionRequest(resources, files, resourceId, path, pointer, expression, body["draft"] as JsonObject), null);
    }

    /// <summary>The title of the refusal for a body, or for a tool input, that is not an expression check.</summary>
    internal const string BadCheck = "The body is not a valid expression check.";

    /// <summary>The title of the refusal for a body that is not a scope request.</summary>
    internal const string BadScope = "The body is not a valid expression scope request.";

    internal const string NoExpressionTitle = "The path holds no expression.";

    /// <summary>
    /// Checks <paramref name="expression"/> at <paramref name="path"/> of the resource <paramref name="resourceId"/> in the compile
    /// of <paramref name="files"/>, as the route does. <paramref name="draft"/>, when given, stands in for the saved resource
    /// at that path. Answers the type and diagnostics, or the title of the refusal:
    /// <see cref="StudioEndpoints.NoResource"/> for a resource the files lack and <see cref="NoExpressionTitle"/> for a path that holds no
    /// expression. Writes nothing.
    /// </summary>
    internal static (StudioExpressionCheckResponse? Answer, string? Refusal) Check(
        IReadOnlyList<StudioFile> resources,
        IReadOnlyList<ResourceContent> files,
        Guid resourceId,
        string path,
        JsonPointer pointer,
        string expression,
        PackageCatalog packages,
        ModuleCatalog modules,
        JsonObject? draft = null)
    {
        var (probe, result, refusal) = Compile(resources, files, resourceId, path, pointer, expression, packages, modules, draft);
        if (probe is null)
        {
            return (null, refusal);
        }

        StudioExpressionDiagnostic? diagnostic = null;
        if (probe.Diagnostic is { } problem)
        {
            var (start, end) = ExpressionRange.Of(expression, problem);
            diagnostic = new StudioExpressionDiagnostic(problem.Code, problem.Message, start, end);
        }
        else if (result!.Diagnostics.FirstOrDefault(
            item => string.Equals(item.File, probe.File, StringComparison.Ordinal) && string.Equals(item.Path, path, StringComparison.Ordinal)) is { } other)
        {
            // A limit the compile reports at the place but not inside the expression check, such as a rule cycle.
            diagnostic = new StudioExpressionDiagnostic(other.Code, other.Message, 0, expression.Length);
        }

        return (
            new StudioExpressionCheckResponse(diagnostic is null ? TypeName(probe.Type) : null, diagnostic is null ? [] : [diagnostic]),
            null);
    }

    /// <summary>
    /// Answers what the expression place at <paramref name="path"/> of the resource <paramref name="resourceId"/> offers: the
    /// type it needs, the fields in scope and the functions it allows, all from the compile's own type check there. The
    /// expression <c>null</c> stands at the path, because it parses and fits every type, so the type check always runs. A place
    /// the compile checks without a type check answers no type, no fields and no functions. Refuses as <see cref="Check"/> does.
    /// Writes nothing.
    /// </summary>
    internal static (StudioExpressionScopeResponse? Answer, string? Refusal) Scope(
        IReadOnlyList<StudioFile> resources,
        IReadOnlyList<ResourceContent> files,
        Guid resourceId,
        string path,
        JsonPointer pointer,
        PackageCatalog packages,
        ModuleCatalog modules,
        JsonObject? draft = null)
    {
        var (probe, _, refusal) = Compile(resources, files, resourceId, path, pointer, "null", packages, modules, draft);
        if (probe is null)
        {
            return (null, refusal);
        }

        if (probe.Place is not { } place)
        {
            return (new StudioExpressionScopeResponse(null, [], []), null);
        }

        return (
            new StudioExpressionScopeResponse(
                place.Expected.Kind == ExpressionTypeKind.Null ? null : TypeName(place.Expected),
                [.. place.Scope.Fields.Select(field => new StudioExpressionScopeField(field.Name, field.LabelKey, TypeName(field.Type)))],
                [.. place.Functions.Select(name => new StudioExpressionScopeFunction(name))]),
            null);
    }

    /// <summary>
    /// Compiles the folder in memory with <paramref name="expression"/> at <paramref name="path"/> of the resource, and answers
    /// the probe that watched the place with the compile's result. Otherwise the title of the refusal.
    /// </summary>
    private static (ExpressionProbe? Probe, CompilationResult? Result, string? Refusal) Compile(
        IReadOnlyList<StudioFile> resources,
        IReadOnlyList<ResourceContent> files,
        Guid resourceId,
        string path,
        JsonPointer pointer,
        string expression,
        PackageCatalog packages,
        ModuleCatalog modules,
        JsonObject? draft)
    {
        if (resources.FirstOrDefault(file => file.Id == resourceId) is not { } found)
        {
            return (null, null, StudioEndpoints.NoResource);
        }

        var edited = Place(found, pointer, expression, draft);
        if (edited is null)
        {
            return (null, null, NoExpressionTitle);
        }

        var checkedFiles = files
            .Select(file => string.Equals(file.Path, found.File, StringComparison.Ordinal) ? new ResourceContent(file.Path, edited) : file)
            .ToList();
        var probe = new ExpressionProbe(found.File, path);
        var result = ApplicationCompiler.CompileFiles(checkedFiles, packages.Versions, modules.Modules, probe);
        return probe.Found ? (probe, result, null) : (null, null, NoExpressionTitle);
    }

    /// <summary>
    /// The resource's JSON, from <paramref name="draft"/> when given and the saved file otherwise, with
    /// <paramref name="expression"/> at <paramref name="pointer"/>: in place of the string there, or as a new property when
    /// the object that would hold it exists. Null when the path is neither.
    /// </summary>
    private static string? Place(StudioFile file, JsonPointer pointer, string expression, JsonObject? draft)
    {
        try
        {
            var content = draft?.DeepClone() ?? JsonNode.Parse(file.Bytes);
            var value = JsonValue.Create(expression);
            PatchOperation operation;
            if (pointer.TryEvaluate(content, out var current))
            {
                if (current is not JsonValue existing || !existing.TryGetValue<string>(out _))
                {
                    return null;
                }

                operation = PatchOperation.Replace(pointer, value);
            }
            else if (pointer.GetParent() is { } holder && holder.TryEvaluate(content, out var parent) && parent is JsonObject)
            {
                operation = PatchOperation.Add(pointer, value);
            }
            else
            {
                return null;
            }

            var result = new JsonPatch(operation).Apply(content);
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
        ExpressionTypeKind.File => "file",
        _ => null,
    };
}

/// <summary>
/// The answer to an expression scope request: the type the place needs, or null when any type will do, the fields in scope
/// in scope order, and the functions the place allows.
/// </summary>
internal sealed record StudioExpressionScopeResponse(
    string? Type, IReadOnlyList<StudioExpressionScopeField> Fields, IReadOnlyList<StudioExpressionScopeFunction> Functions);

/// <summary>One name in scope. <c>LabelKey</c> is null for a name with no label, such as a parameter.</summary>
internal sealed record StudioExpressionScopeField(string Name, string? LabelKey, string? Type);

/// <summary>One function or aggregate a place allows.</summary>
internal sealed record StudioExpressionScopeFunction(string Name);

/// <summary>The answer to an expression check: the expression's type, and the problem with its range when there is one.</summary>
internal sealed record StudioExpressionCheckResponse(string? Type, IReadOnlyList<StudioExpressionDiagnostic> Diagnostics);

/// <summary>One problem of an expression. <c>Start</c> and <c>End</c> are UTF-16 offsets into the expression, and <c>End</c> is exclusive.</summary>
internal sealed record StudioExpressionDiagnostic(string Code, string Message, int Start, int End);
