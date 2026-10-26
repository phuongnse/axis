using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Core.Tenancy;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Exports;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Npgsql;

namespace Axis.Server.Records;

/// <summary>
/// Endpoints for the records of an entity in the active release of an application. Every request
/// is checked in order: session or bearer token (401), then the path (404), then the user's grant for
/// the entity and the action (403), and only then the query or body. A request without a session or with
/// a bad bearer token is a 401 before the path is read, so it learns nothing about what exists.
/// A request with an <c>Authorization: Bearer</c> header is a service account request. It is judged by the
/// token alone, never reads or renews the session cookie and needs no CSRF header. Its roles come from the
/// token, and policies, record filters and field limits apply as for a user. A 401 for it carries
/// <c>WWW-Authenticate: Bearer</c>. In the text below, the session user stands for the service account too.
/// A path that names no active application, entity or record is a 404; a child entity is no entity here.
/// A list, a read and a history need <c>read</c>, and a create, an update and a delete need that action.
/// A grant with a record filter reaches only the records that match it, and the grants combine
/// with OR. A list holds only the records that match a <c>read</c> filter, and a single read, a
/// history, an update or a delete of any other record is a 404, as is an update or a delete by a
/// user with no <c>read</c> grant. An update or a delete of a readable record that matches no
/// <c>update</c> or <c>delete</c> filter is a 403. These checks come after the path and before the
/// body. A create or update whose record as stored matches no <c>create</c> or <c>update</c>
/// filter is a 403 too: it is checked in the write transaction, after storage, and rolls the write
/// back. A filter that fails for a record grants nothing through its grant and is logged, and a
/// list whose filters fail is a 400.
/// A grant with <c>fields</c> covers only those fields. A record, in a list, a single read or the
/// response of a create or update, holds only the values the user's <c>read</c> grants cover, and a
/// reference label only when the user may also read the target's display field. A history leaves
/// the other field names out of the details. A sort by a field the user may not read is a 400, as
/// is a search when they may not read the display field. A search skips a searchable field they may
/// not read.
/// A create or update writes the record and its child rows in one transaction. A create sets the
/// entity's creator fields to the session user's id. Every create, update and delete that
/// succeeds appends one audit record in its transaction. Its actor is the session user, with their
/// display name. A write that fails leaves none.
/// A create or update also starts the processes of the data change triggers that match it, in the
/// same transaction, as <c>system</c>. A write that fails starts none.
/// A create or update may name a form of the entity in <c>?form=</c>. The name is checked after the
/// grant and record checks and before the content type: a name that is no form of the entity, and
/// an empty or repeated one, is a 400 at <c>form</c>.
/// A body is checked in order: content type (415), then body (400), then a field that no grant for
/// the action covers (403, at the path of each such field), then the computed fields of
/// the record as it will be stored and of its rows (400), then the form's <c>requiredWhen</c>
/// conditions and the entity's validations on that record (400, in one response, where
/// "Required." wins over a validation message on the same field), then storage (404, 409). An
/// update of an entity with computed fields or validations, or with a form that has
/// <c>requiredWhen</c> conditions, reads the stored record first, so an unknown record is a 404 there.
/// A create, update or delete that finds a different active release under the start lock runs again from
/// the path check on the new release, and answers 503 after three attempts. Nothing is written by
/// an attempt that finds a change.
/// A delete is 204, or 404 or 409 from storage. It is also a 409 when a running or waiting process works on
/// the record. A record's history is checked as a single read
/// (404), then paging (400), and an unknown or deleted record is a 404.
/// A <c>read</c> filter of the user's grants that reads a computed field that is being recomputed
/// makes a list, a single read and a history a 409 that names no field or policy. It comes before
/// the query and before any 404, so it also covers an unknown id. An update or a delete is the same
/// 409 before its 404 for a <c>read</c> filter, and after the 404 and before the 403 for an
/// <c>update</c> or <c>delete</c> filter. Nothing is written. A list whose valid <c>sort</c> or
/// <c>search</c> looks in such a field is a 409 with the problem at <c>sort</c> or <c>search</c>. The
/// problem titles never contain text from the request.
/// An entity of a module is written <c>{module id}/{entity name}</c>, as two path segments, for
/// example <c>entities/departments/Department/records</c>. It has the same routes and checks as
/// any other entity, and only that form reaches it.
/// </summary>
internal static class RecordEndpoints
{
    public static void MapRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // An entity of the application has one segment, and an entity of a module has the module id
        // and the entity name as two, so no entity has two URLs.
        foreach (var pattern in new[] { "/api/apps/{app}/entities/{entity}/records", "/api/apps/{app}/entities/{module}/{entity}/records" })
        {
            var records = endpoints.MapGroup(pattern).WithMetadata(ServiceAccountRoute.Instance);
            records.MapGet("", ListAsync).WithQueryTimeout(QueryTimeoutSettings.Interactive);
            records.MapGet("/{id}", GetAsync).WithQueryTimeout(QueryTimeoutSettings.Interactive);
            records.MapGet("/{id}/history", HistoryAsync).WithQueryTimeout(QueryTimeoutSettings.Interactive);
            records.MapPost("", CreateAsync);
            records.MapPatch("/{id}", UpdateAsync);
            records.MapDelete("/{id}", DeleteAsync);

            // Only a signed-in user exports, so a service account never reaches it. The literal segment wins over {id}.
            endpoints.MapGroup(pattern).MapGet("/export", ExportAsync).WithQueryTimeout(QueryTimeoutSettings.Export);
        }
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    // A repeated parameter binds as its values joined by commas, which never parses. That text
    // is a valid search, so a repeated search is found on the request instead.
    private static async Task<IResult> ListAsync(
        string app,
        string entity,
        string? page,
        string? pageSize,
        string? sort,
        string? search,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        var (application, model, notFound, _) = await ResolveEntityAsync(app, EntityName(request.HttpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return Grants.Forbidden();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application!, model, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        var fields = RecordFieldAccess.Read(application!, user.Roles, model);
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);

        var (order, searched) = ReadQuery(sort, search, request, model, fields, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (await PendingQueryAsync(connection, database.ReadTransaction, model, order, searched, cancellationToken) is { } pendingQuery)
        {
            return pendingQuery;
        }

        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application!, model, Expressions(filters), user.Id)
            : null;
        if (await RecordQueries.ListAsync(connection, database.ReadTransaction, model, pageNumber, size, order, searched, condition, cancellationToken) is not { } result)
        {
            // PostgreSQL cannot fail one branch of an OR, so every filter of the list is named.
            var logger = Logger(loggerFactory);
            foreach (var filter in read.Filters!)
            {
                RecordFilterLog.ListFailed(logger, filter.Policy, filter.Grant, model.Name);
            }

            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A record filter could not be evaluated for these records.");
        }

        return Results.Ok(new RecordListResponse([.. result.Items.Select(fields.Visible)], pageNumber, size, result.TotalCount));
    }

    // An export takes the query of the list without its paging. The grant, the filters and the field
    // checks are the list's, and the export grant is checked first. The rows are written to a temporary
    // file, so the file is complete before the audit record and before any byte is sent.
    private static async Task<IResult> ExportAsync(
        string app,
        string entity,
        string? sort,
        string? search,
        string? format,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ExportSettings export,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        var (application, model, notFound, _) = await ResolveEntityAsync(app, EntityName(request.HttpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!PolicyEvaluator.AllowsExport(application!, user.Roles, model) || !read.Granted)
        {
            return Grants.Forbidden();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application!, model, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        var fields = RecordFieldAccess.Read(application!, user.Roles, model);
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var (order, searched) = ReadQuery(sort, search, request, model, fields, errors);
        ExportFile.TryParseFormat(format, errors, out var exportFormat);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (await PendingQueryAsync(connection, database.ReadTransaction, model, order, searched, cancellationToken) is { } pendingQuery)
        {
            return pendingQuery;
        }

        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application!, model, Expressions(filters), user.Id)
            : null;

        // A child collection and a references field have no column to export.
        var columns = model.Fields.Where(field => field.HasColumn && fields.CanRead(field.Name)).ToList();
        string?[] Cells(Record record)
        {
            var visible = fields.Visible(record);
            return [.. columns.Select(column => ExportValues.Format(
                column.Type, visible.Values.GetValueOrDefault(column.Name), visible.Labels.GetValueOrDefault(column.Name)))];
        }

        try
        {
            return await ExportFile.RunAsync(
                request.HttpContext,
                database,
                connection,
                exportFormat,
                model.Name,
                [.. columns.Select(column => new ExportColumn(column.Name, column.Type))],
                RecordQueries.ReadAllAsync(connection, model, order, searched, condition, export.MaxRows + 1, cancellationToken),
                Cells,
                export.MaxRows,
                (rows, truncated) => new AuditEntry(
                    user.Id,
                    AuditActions.RecordsExported,
                    application!.Manifest.Id,
                    model.Id,
                    null,
                    null,
                    new JsonObject { ["format"] = exportFormat.Name(), ["rowCount"] = rows, ["truncated"] = truncated },
                    user.DisplayName),
                cancellationToken);
        }
        catch (PostgresException exception) when (condition is not null && exception.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            // Class 22 is a data exception. PostgreSQL cannot fail one branch of an OR, so every filter is named.
            var logger = Logger(loggerFactory);
            foreach (var filter in read.Filters!)
            {
                RecordFilterLog.ListFailed(logger, filter.Policy, filter.Grant, model.Name);
            }

            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A record filter could not be evaluated for these records.");
        }
    }

    // A field the user may not read is no sort, and a hidden display field is no search. A hidden
    // searchable field is left out of the search, so neither can probe a hidden value. Every
    // invalid parameter adds an error.
    private static (RecordSort? Order, RecordSearch? Search) ReadQuery(
        string? sort, string? search, HttpRequest request, EntityModel model, RecordFieldAccess fields, SortedDictionary<string, string[]> errors)
    {
        RecordSort? order = null;
        RecordSearch? searched = null;
        if (sort is not null && (!RecordSort.TryParse(sort, model, out order) || !fields.CanRead(order.Field.Name)))
        {
            errors["sort"] = ["Must be a declared field name, optionally preceded by '-'."];
        }

        if (SearchQuery.IsRepeated(request))
        {
            errors["search"] = SearchQuery.RepeatedError;
        }
        else if (!string.IsNullOrEmpty(search))
        {
            if (model.DisplayField is null)
            {
                errors["search"] = ["The entity has no display field to search."];
            }
            else if (!fields.CanRead(model.DisplayField))
            {
                errors["search"] = ["You may not read the display field."];
            }
            else if (SearchQuery.InvalidError(search) is { } invalid)
            {
                errors["search"] = invalid;
            }
            else
            {
                searched = new RecordSearch(search, RecordSearch.Searched(model, fields.CanRead));
            }
        }

        return (order, searched);
    }

    // A sort or a search on a computed field that is being recomputed would order or match by a stale stored value.
    private static async Task<IResult?> PendingQueryAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, EntityModel model, RecordSort? order, RecordSearch? search, CancellationToken cancellationToken)
    {
        var pending = await PendingComputedFields.ReadAsync(connection, transaction, [model.Id], cancellationToken);
        if (!pending.TryGetValue(model.Id, out var pendingNames))
        {
            return null;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (order is not null && pendingNames.Contains(order.Field.Name))
        {
            errors["sort"] = [$"The field '{order.Field.Name}' is being recomputed."];
        }

        if (search is not null
            && search.Fields.Where(field => pendingNames.Contains(field.Name)).Select(field => $"The field '{field.Name}' is being recomputed.").ToArray() is { Length: > 0 } pendingSearched)
        {
            errors["search"] = pendingSearched;
        }

        return errors.Count > 0
            ? Results.ValidationProblem(errors, statusCode: StatusCodes.Status409Conflict, title: "A field is being recomputed.")
            : null;
    }

    private static async Task<IResult> GetAsync(
        string app,
        string entity,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        var (application, model, notFound, _) = await ResolveEntityAsync(app, EntityName(httpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return Grants.Forbidden();
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application!, model, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        if (read.Filters is { } filters
            && await RecordCommand.MatchAsync(connection, database.ReadTransaction, application!, model, recordId, filters, user.Id, loggerFactory, cancellationToken) is not { Found: true, Matched: true })
        {
            return RecordNotFound();
        }

        if (await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken) is not { } record)
        {
            return RecordNotFound();
        }

        // A computed field that is pending shows as the active release computes it. The read stores nothing.
        record = await PendingComputedReads.ApplyAsync(connection, database.ReadTransaction, application!, model, record, cancellationToken);
        return Results.Ok(RecordFieldAccess.Read(application!, user.Roles, model).Visible(record));
    }

    // Every audit record that names the record is listed, whoever wrote it: the record API, a
    // process start, a step or a task decision. The actor's display name is the one stored with
    // the audit record, so it is null for system, anonymous and audit records written before
    // names were stored. inspectPath is the instance's detail path when the user may inspect its
    // process, and null otherwise.
    private static async Task<IResult> HistoryAsync(
        string app,
        string entity,
        string id,
        string? page,
        string? pageSize,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        var (application, model, notFound, _) = await ResolveEntityAsync(app, EntityName(httpContext, entity), resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return Grants.Forbidden();
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application!, model, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (!PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size))
        {
            return Results.ValidationProblem(errors);
        }

        var readable = read.Filters is { } filters
            ? await RecordCommand.MatchAsync(connection, database.ReadTransaction, application!, model, recordId, filters, user.Id, loggerFactory, cancellationToken) is { Found: true, Matched: true }
            : await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken) is not null;
        if (!readable)
        {
            return RecordNotFound();
        }

        var fields = RecordFieldAccess.Read(application!, user.Roles, model);
        var result = await AuditRecords.ListForRecordAsync(connection, model.Id, recordId, pageNumber, size, cancellationToken);
        var instanceIds = result.Items
            .Where(item => item.ProcessInstanceId is not null)
            .Select(item => item.ProcessInstanceId!.Value)
            .Distinct()
            .ToList();
        var processIds = await InstanceQueries.ProcessIdsAsync(connection, application!.Manifest.Id, instanceIds, cancellationToken);
        var items = result.Items
            .Select(item => new RecordHistoryItem(
                item.Id,
                item.OccurredAt.UtcDateTime,
                item.Actor,
                item.ActorName,
                item.Action,
                item.ProcessInstanceId,
                VisibleDetails(JsonNode.Parse(item.Details)!, fields),
                InspectPath(application, user.Roles, item.ProcessInstanceId, processIds)))
            .ToList();
        return Results.Ok(new RecordHistoryResponse(items, pageNumber, size, result.TotalCount));
    }

    // The path is set only when the instance's process is in the active release and the user holds
    // inspect on it, as the inspection endpoints check, so a path always leads to an answer.
    private static string? InspectPath(
        ApplicationModel application,
        IEnumerable<string> roles,
        Guid? instanceId,
        IReadOnlyDictionary<Guid, Guid> processIds)
    {
        if (instanceId is not { } instance || !processIds.TryGetValue(instance, out var processId))
        {
            return null;
        }

        var process = application.Processes.FirstOrDefault(candidate => candidate.Id == processId);
        return process is not null && PolicyEvaluator.AllowsInspect(application, roles, process)
            ? $"/api/apps/{application.Manifest.Name}/processes/{process.Name}/instances/{instance:D}"
            : null;
    }

    // The body is read through a callback, so the command reads it only after the grant and filter
    // checks. The parser reports every body problem by JSON Pointer, and the content type is
    // checked before any of the body is read.
    private static async Task<IResult> CreateAsync(
        string app,
        string entity,
        string? form,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        // A release change under the start lock sends the create back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            var (application, model, notFound, releaseId) = await ResolveEntityAsync(app, EntityName(request.HttpContext, entity), resolver, cancellationToken);
            if (model is null)
            {
                return notFound!;
            }

            var connection = await database.GetConnectionAsync(cancellationToken);
            var result = await RecordCommand.CreateAsync(
                connection,
                TenantId(tenants),
                application!,
                releaseId,
                model,
                Actor(user),
                BodyReader(request),
                form,
                loggerFactory,
                cancellationToken);
            if (result.Outcome == RecordCommandOutcome.ReleaseChanged)
            {
                continue;
            }

            // The location uses the model's names, so a record has one URL whatever case the caller used.
            return result is { Outcome: RecordCommandOutcome.Written, Record: { } created }
                ? Results.Created(
                    $"/api/apps/{application!.Manifest.Name}/entities/{model.Name}/records/{created.Id:D}",
                    RecordFieldAccess.Read(application, user.Roles, model).Visible(created))
                : WriteFailure(result);
        }

        return ReleaseRetry.Changed();
    }

    private static async Task<IResult> UpdateAsync(
        string app,
        string entity,
        string id,
        string? form,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        // A release change under the start lock sends the update back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            var (application, model, notFound, releaseId) = await ResolveEntityAsync(app, EntityName(request.HttpContext, entity), resolver, cancellationToken);
            if (model is null)
            {
                return notFound!;
            }

            // The grant is checked before the id, so an id that is no record answers 403 to an actor
            // with no grant and 404 to one with it.
            if (!TryParseId(id, out var recordId))
            {
                return UnparsedId(application!, user, model, PolicyAction.Update);
            }

            var connection = await database.GetConnectionAsync(cancellationToken);
            var result = await RecordCommand.UpdateAsync(
                connection,
                TenantId(tenants),
                application!,
                releaseId,
                model,
                recordId,
                Actor(user),
                BodyReader(request),
                form,
                loggerFactory,
                cancellationToken);
            if (result.Outcome == RecordCommandOutcome.ReleaseChanged)
            {
                continue;
            }

            return result is { Outcome: RecordCommandOutcome.Written, Record: { } updated }
                ? Results.Ok(RecordFieldAccess.Read(application!, user.Roles, model).Visible(updated))
                : WriteFailure(result);
        }

        return ReleaseRetry.Changed();
    }

    private static async Task<IResult> DeleteAsync(
        string app,
        string entity,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        // A release change under the start lock sends the delete back to resolution, so the delete triggers come from the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            var (application, model, notFound, releaseId) = await ResolveEntityAsync(app, EntityName(httpContext, entity), resolver, cancellationToken);
            if (model is null)
            {
                return notFound!;
            }

            if (!TryParseId(id, out var recordId))
            {
                return UnparsedId(application!, user, model, PolicyAction.Delete);
            }

            var connection = await database.GetConnectionAsync(cancellationToken);
            var result = await RecordCommand.DeleteAsync(
                connection, TenantId(tenants), application!, releaseId, model, recordId, Actor(user), loggerFactory, cancellationToken);
            if (result.Outcome == RecordCommandOutcome.ReleaseChanged)
            {
                continue;
            }

            return result.Outcome == RecordCommandOutcome.Deleted ? Results.NoContent() : WriteFailure(result);
        }

        return ReleaseRetry.Changed();
    }

    private static RecordActor Actor(CurrentUserResponse user) => new(user.Id, user.DisplayName, user.Roles);

    // The route of a module entity has a module segment before the entity name.
    internal static string EntityName(HttpContext context, string entity) =>
        context.GetRouteValue("module") is string module ? $"{module}/{entity}" : entity;

    private static RecordBodyReader BodyReader(HttpRequest request) =>
        async cancellationToken => JsonRequest.IsJson(request) ? await ReadBodyAsync(request, cancellationToken) : null;

    // An id that names no record is a 403 when the user has no grant for the action, as the grant
    // comes before the id, and a 404 otherwise.
    private static IResult UnparsedId(ApplicationModel application, CurrentUserResponse user, EntityModel model, PolicyAction action) =>
        PolicyEvaluator.Access(application, user.Roles, model, action).Granted ? RecordNotFound() : Grants.Forbidden();

    private static List<ExpressionModel> Expressions(IReadOnlyList<RecordFilter> filters) => RecordCommand.Expressions(filters);

    private static ILogger Logger(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger(typeof(RecordEndpoints).FullName!);

    /// <summary>
    /// Finds the application and the entity in its active release, or the 404 that says which is
    /// missing. A child entity has no record routes: its rows are served through the owner record.
    /// </summary>
    internal static async Task<(ApplicationModel? Application, EntityModel? Entity, IResult? NotFound, Guid ReleaseId)> ResolveEntityAsync(
        string app,
        string entity,
        ActiveApplicationResolver resolver,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return (null, null, NotFound("No application is active under this name."), Guid.Empty);
        }

        var application = active.Model;
        return application.TryGetEntity(entity, out var model) && !application.IsChildEntity(model)
            ? (application, model, null, active.ReleaseId)
            : (null, null, NotFound("The application has no entity with this name."), Guid.Empty);
    }

    private static string TenantId(ITenantContextAccessor tenants) =>
        (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;

    /// <summary>
    /// The details of an audit record with only the field names the user may read in its
    /// <c>fields</c>, as a <c>record.updated</c> or a <c>task.completed</c> has.
    /// </summary>
    private static JsonNode VisibleDetails(JsonNode details, RecordFieldAccess fields)
    {
        if (fields.Readable is not null && details is JsonObject { } holder && holder["fields"] is JsonArray names)
        {
            holder["fields"] = new JsonArray([.. names
                .Select(name => name is JsonValue value && value.TryGetValue<string>(out var text) ? text : null)
                .Where(name => name is not null && fields.CanRead(name))
                .Select(name => JsonValue.Create(name))]);
        }

        return details;
    }

    // Only the hyphenated form names a record; any other text is no record, not a bad request.
    private static bool TryParseId(string id, out Guid recordId) =>
        Guid.TryParseExact(id, "D", out recordId) && id.Length == 36;

    // Buffered, so a write that runs again on a new release can read the body again.
    internal static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        request.Body.Position = 0;
        return body.ToArray();
    }

    internal static IResult WriteFailure(RecordCommandResult result) =>
        result.Outcome switch
        {
            RecordCommandOutcome.Denied => Grants.Forbidden(),
            RecordCommandOutcome.OutsideFilters => Grants.OutsideFilter(),
            RecordCommandOutcome.UnwritableFields => Grants.UnwritableFields(result.Errors!),
            RecordCommandOutcome.NotFound => RecordNotFound(),
            RecordCommandOutcome.UnsupportedContent => JsonRequest.UnsupportedMediaType(),
            RecordCommandOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            RecordCommandOutcome.StaleVersion => Conflict("The record has changed since this version was read."),
            RecordCommandOutcome.ReleaseChanged => ReleaseRetry.Changed(),
            RecordCommandOutcome.PendingFilter => Grants.PendingFilter(),
            RecordCommandOutcome.Conflict => result.Conflict switch
            {
                RecordConflict.UniqueValue => Results.ValidationProblem(
                    result.Errors!,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "A value must be unique."),
                // Names no table or constraint: the referencing entity is not part of the response.
                RecordConflict.Referenced => Conflict("Another record references this record."),
                // Names no column or constraint: the storage names are not part of the API.
                RecordConflict.Schema => Conflict("The stored schema does not match the active model."),
                // Names no process, instance or step: the process is not part of the response.
                RecordConflict.RunningProcess => Conflict("A running process works on this record."),
                var conflict => throw new InvalidOperationException($"Unexpected conflict {conflict}."),
            },
            var outcome => throw new InvalidOperationException($"Unexpected record command outcome {outcome}."),
        };

    internal static IResult WriteFailure(RecordWriteResult result) =>
        result.Outcome switch
        {
            RecordWriteOutcome.NotFound => RecordNotFound(),
            RecordWriteOutcome.StaleVersion => Conflict("The record has changed since this version was read."),
            RecordWriteOutcome.MissingReference or RecordWriteOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            RecordWriteOutcome.UniqueViolation => Results.ValidationProblem(
                result.Errors!,
                statusCode: StatusCodes.Status409Conflict,
                title: "A value must be unique."),
            // Names no column or constraint: the storage names are not part of the API.
            RecordWriteOutcome.SchemaConflict => Conflict("The stored schema does not match the active model."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unexpected write outcome."),
        };

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static IResult RecordNotFound() => NotFound("No record has this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>
/// One page of a record list and the number of records that match the search, or of every record
/// without one. The number is <see langword="null"/> when the count ran past the query timeout.
/// </summary>
internal sealed record RecordListResponse(IReadOnlyList<Record> Items, int Page, int PageSize, long? TotalCount);

/// <summary>One page of a record's audit records, newest first, and the number of its audit records.</summary>
internal sealed record RecordHistoryResponse(IReadOnlyList<RecordHistoryItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// One audit record of a record. <see cref="ActorName"/> is the actor's display name as stored with
/// the audit record, or null for <c>system</c>, <c>anonymous</c> and audit records written before
/// names were stored. <see cref="InspectPath"/> is the detail path of the process instance that wrote
/// the audit record, or null when it has none, its process is not in the active release, or the user
/// holds no <c>inspect</c> grant on the process.
/// </summary>
internal sealed record RecordHistoryItem(
    Guid Id,
    DateTime OccurredAt,
    string Actor,
    string? ActorName,
    string Action,
    Guid? ProcessInstanceId,
    JsonNode Details,
    string? InspectPath);
