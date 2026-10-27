using Axis.Configuration.Model;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Work;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Records;

/// <summary>
/// Endpoints for the imports of an entity: the upload of a CSV file or an <c>xlsx</c> workbook, the state of an
/// upload, and the commit of a checked upload. Only a
/// signed-in user uses them, never a service account. Every request is checked in order: session (401), then the
/// path (404), then the user's grant (403), and only then the body. An upload needs the <c>import</c> grant and a
/// <c>create</c> grant on the entity, and the <c>update</c> grant too when it names a <c>key</c>. A <c>key</c>
/// that is not the name of a unique field of the entity is a 400 at <c>key</c>. The file is the raw request
/// body with the content type <c>text/csv</c> or the <c>xlsx</c> content type, at most <c>Data:ImportMaxBytes</c>
/// bytes: another content type is a 415 and a larger body a 413. The upload does not look inside the file, so a
/// body that is not a workbook fails its dry run. The upload stores the import <c>pending</c>, queues the work item that
/// dry-runs it, and answers 202 with the import and its <c>Location</c>. The worker then checks every row as a
/// create would, and writes no record. An import is read by the user who uploaded it, and
/// any other user, or an unknown id, gets a 404. The commit checks the session (401), the path (404), the user's
/// current <c>import</c> and <c>create</c> grants (403), then the import (404 for another user's or an unknown
/// one) and its state: only a <c>checked</c> import with no commit queued is committed, and any other state is a
/// 409. A <c>checked</c> import whose file was deleted after 24 hours is a 409 too. It queues the work item that writes the rows and answers 202 with the import, still <c>checked</c>.
/// An entity of a module has the same routes as in <see cref="RecordEndpoints"/>.
/// </summary>
internal static class RecordImportEndpoints
{
    public static void MapRecordImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        foreach (var pattern in new[] { "/api/apps/{app}/entities/{entity}/imports", "/api/apps/{app}/entities/{module}/{entity}/imports" })
        {
            var imports = endpoints.MapGroup(pattern);
            imports.MapPost("", UploadAsync);
            imports.MapGet("/{id}", GetAsync).WithQueryTimeout(QueryTimeoutSettings.Interactive);
            imports.MapPost("/{id}/commit", CommitAsync);
        }
    }

    private static async Task<IResult> UploadAsync(
        string app,
        string entity,
        string? key,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        RecordImportSettings settings,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        var (application, model, notFound, _) = await RecordEndpoints.ResolveEntityAsync(app, RecordEndpoints.EntityName(request.HttpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        if (!PolicyEvaluator.AllowsImport(application!, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        if (key is not null && !PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Update).Granted)
        {
            return Grants.Forbidden();
        }

        if (key is not null && !model.Fields.Any(field => field.Unique && field.HasColumn && field.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["key"] = ["Must be the name of a unique field of the entity."] });
        }

        if (Format(request) is not { } format)
        {
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "The request body must be text/csv or an xlsx workbook.");
        }

        if (await ReadFileAsync(request, settings.MaxBytes, cancellationToken) is not { } file)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The file is larger than the import limit.");
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var connection = await database.GetConnectionAsync(cancellationToken);
        var id = Guid.CreateVersion7();
        var created = DateTimeOffset.UtcNow;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var itemId = await WorkItemQueue.EnqueueAsync(connection, transaction, tenantId, RecordImports.CheckWorkItemKind, created, cancellationToken);
            await RecordImports.InsertAsync(
                connection,
                transaction,
                new RecordImport(id, tenantId, application!.Manifest.Id, model.Id, user.Id, user.DisplayName, user.Roles, key, format, file, itemId, created),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // The location uses the model's names, so an import has one URL whatever case the caller used.
        return Results.Accepted(
            $"/api/apps/{application.Manifest.Name}/entities/{model.Name}/imports/{id:D}",
            new RecordImportResponse(id, RecordImportStates.Pending, key, new Dictionary<string, string[]>(), created.UtcDateTime));
    }

    private static async Task<IResult> GetAsync(
        string app,
        string entity,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        var (application, model, notFound, _) = await RecordEndpoints.ResolveEntityAsync(app, RecordEndpoints.EntityName(httpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        // Only the hyphenated form names an import, as for a record.
        if (!Guid.TryParseExact(id, "D", out var importId) || id.Length != 36)
        {
            return ImportNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await RecordImports.FindAsync(connection, importId, application!.Manifest.Id, model.Id, user.Id, cancellationToken) is { } import
            ? Results.Ok(new RecordImportResponse(import.Id, import.State, import.Key, import.Errors, import.CreatedAt.UtcDateTime))
            : ImportNotFound();
    }

    private static async Task<IResult> CommitAsync(
        string app,
        string entity,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        var (application, model, notFound, _) = await RecordEndpoints.ResolveEntityAsync(app, RecordEndpoints.EntityName(httpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        // The worker writes with the roles stored at upload, so the user's current grants are checked here.
        if (!PolicyEvaluator.AllowsImport(application!, user.Roles, model) || !PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Create).Granted)
        {
            return Grants.Forbidden();
        }

        if (!Guid.TryParseExact(id, "D", out var importId) || id.Length != 36)
        {
            return ImportNotFound();
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var connection = await database.GetConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        RecordImportCommitQueued queued;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var itemId = await WorkItemQueue.EnqueueAsync(connection, transaction, tenantId, RecordImports.CommitWorkItemKind, now, cancellationToken);
            queued = await RecordImports.QueueCommitAsync(connection, transaction, importId, application!.Manifest.Id, model.Id, user.Id, itemId, cancellationToken);
            if (queued == RecordImportCommitQueued.Queued)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        if (queued == RecordImportCommitQueued.NotFound)
        {
            return ImportNotFound();
        }

        if (queued == RecordImportCommitQueued.WrongState)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only a checked import with no commit queued can be committed.");
        }

        if (queued == RecordImportCommitQueued.FileDeleted)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The file of this import was deleted.");
        }

        var import = await RecordImports.FindAsync(connection, importId, application.Manifest.Id, model.Id, user.Id, cancellationToken)
            ?? throw new InvalidOperationException("The queued import is gone.");
        return Results.Accepted(
            $"/api/apps/{application.Manifest.Name}/entities/{model.Name}/imports/{importId:D}",
            new RecordImportResponse(import.Id, import.State, import.Key, import.Errors, import.CreatedAt.UtcDateTime));
    }

    private const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // The format the content type names, or null for any other. A CSV file is text/csv with at most a utf-8
    // charset, as the reader reads UTF-8 only. A workbook is the xlsx media type with no parameter.
    private static string? Format(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType))
        {
            return null;
        }

        if (contentType.MediaType.Equals(XlsxMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return contentType.Parameters.Count == 0 ? RecordImportFormats.Xlsx : null;
        }

        return contentType.MediaType.Equals("text/csv", StringComparison.OrdinalIgnoreCase)
            && contentType.Parameters.All(parameter =>
                parameter.Name.Equals("charset", StringComparison.OrdinalIgnoreCase)
                && HeaderUtilities.RemoveQuotes(parameter.Value).Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            ? RecordImportFormats.Csv
            : null;
    }

    // Reads the body, or returns null when it is larger than maxBytes. A body that announces more is refused
    // before a byte is read, and one that does not is cut off at maxBytes + 1.
    private static async Task<byte[]?> ReadFileAsync(HttpRequest request, long maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false } && (feature.MaxRequestBodySize is null || feature.MaxRequestBodySize < maxBytes + 1))
        {
            feature.MaxRequestBodySize = maxBytes + 1;
        }

        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > maxBytes)
            {
                return null;
            }

            body.Write(buffer, 0, read);
        }

        return body.ToArray();
    }

    private static IResult ImportNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No import has this id.");
}

/// <summary>
/// An import as its uploader reads it. <see cref="Errors"/> hold the problems of a failed dry run by JSON Pointer,
/// <c>/rows/{n}/values/{field}</c> for a cell, and are empty for any other state.
/// </summary>
internal sealed record RecordImportResponse(Guid Id, string State, string? Key, IReadOnlyDictionary<string, string[]> Errors, DateTime CreatedAt);
