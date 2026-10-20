using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;
using Axis.Configuration.Templates;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Studio;

/// <summary>
/// <c>POST /api/studio/apps</c> creates an application folder in <c>Studio:ApplicationsPath</c> from <c>{ name, template? }</c>
/// and answers 201 with <c>{ name, diagnostics }</c> and a <c>Location</c> header. The order of checks is the session (401),
/// the author role (403), a set <c>Studio:ApplicationsPath</c> (403), the content type (415), the body (400), the template
/// (404), the write lock (503), then a name or folder that exists (409). The CSRF header is checked by the middleware first.
/// The problem titles never contain text from the request. The route never activates a release.
/// </summary>
internal static class StudioApplicationEndpoints
{
    public static void MapStudioApplicationEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/studio/apps", CreateAsync);

    private static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        StudioOptions options,
        TemplateCatalog templates,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock,
        IStudioFileWriter writer)
    {
        if (await StudioEndpoints.RefuseAsync(httpContext, options) is { } refusal)
        {
            return refusal;
        }

        if (options.ApplicationsPath is null)
        {
            return StudioEndpoints.Problem(StatusCodes.Status403Forbidden, "Studio cannot create applications here.");
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        JsonNode? body;
        try
        {
            body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body is not valid JSON.");
        }

        if (body is not JsonObject content || content.Any(pair => pair.Key is not ("name" or "template")))
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body must be an object with a name and an optional template.");
        }

        if (content["name"] is not JsonValue nameValue
            || !nameValue.TryGetValue<string>(out var name)
            || !StudioFileNames.IsName(name))
        {
            return StudioEndpoints.Problem(
                StatusCodes.Status400BadRequest, "The name must be letters and digits, start with a letter and have at most 60 characters.");
        }

        var kebab = StudioFileNames.Kebab(name);
        if (kebab.Length > SitePaths.MaxLength || SitePaths.Reserved.Contains(kebab))
        {
            return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The site path of the name is too long or reserved.");
        }

        string? templateId = null;
        if (content.ContainsKey("template"))
        {
            if (content["template"] is not JsonValue templateValue || !templateValue.TryGetValue<string>(out templateId))
            {
                return StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The template must be a template id.");
            }
        }

        InstalledTemplate? template = null;
        if (templateId is not null)
        {
            template = templates.Newest.FirstOrDefault(candidate => string.Equals(candidate.Manifest.Id, templateId, StringComparison.Ordinal));
            if (template is null)
            {
                return StudioEndpoints.NotFound("No installed template has this id.");
            }
        }

        var files = template is null ? StudioApplications.Blank(name) : StudioApplications.FromTemplate(template, name);
        var outcome = await StudioApplications.CreateAsync(writeLock, writer, options, name, files, httpContext.RequestAborted);
        switch (outcome)
        {
            case StudioCreateOutcome.Unavailable:
                return StudioEndpoints.Problem(StatusCodes.Status503ServiceUnavailable, StudioEndpoints.WriteLockUnavailable);
            case StudioCreateOutcome.Exists:
                return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "An application with this name or folder already exists.");
        }

        var target = Path.Combine(Path.TrimEndingDirectorySeparator(options.ApplicationsPath), kebab);
        return Results.Created(
            $"/api/studio/apps/{Uri.EscapeDataString(name)}/resources",
            new StudioCreateApplicationResponse(name, StudioDiagnostics.Compile(target, packages, modules)));
    }
}

/// <summary>The answer of a create: the application's <c>name</c> and the diagnostics of its folder.</summary>
internal sealed record StudioCreateApplicationResponse(string Name, IReadOnlyList<StudioDiagnostic> Diagnostics);
