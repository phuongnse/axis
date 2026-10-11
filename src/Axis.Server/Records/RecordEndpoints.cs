using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Npgsql;

namespace Axis.Server.Records;

/// <summary>
/// Endpoints for the records of an entity in the active release of an application. Every request
/// is checked in order: session (401), then the path (404), then the user's grant for the entity
/// and the action (403), and only then the query or body. A request without a session is a 401
/// before the path is read, so it learns nothing about what exists. A path that names no active
/// application, entity or record is a 404; a child entity is no entity here. A list, a read and a
/// history need <c>read</c>, and a create, an update and a delete need that action.
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
/// is a search when they may not read the display field.
/// A create or update writes the record and its child rows in one transaction. A create sets the
/// entity's creator fields to the session user's id. Every create, update and delete that
/// succeeds appends one audit record in its transaction. Its actor is the session user, with their
/// display name. A write that fails leaves none.
/// A body is checked in order: content type (415), then body (400), then a field that no grant for
/// the action covers (403, at the path of each such field), then the computed fields of
/// the record as it will be stored and of its rows (400), then the entity's validations on that
/// record (400), then storage (404, 409). An update of an entity with computed fields or
/// validations reads the stored record first, so an unknown record is a 404 there.
/// A delete is 204, or 404 or 409 from storage. A record's history is checked as a single read
/// (404), then paging (400), and an unknown or deleted record is a 404. The problem titles never
/// contain text from the request.
/// </summary>
internal static class RecordEndpoints
{
    private const int MaxSearchLength = 200;

    public static void MapRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var records = endpoints.MapGroup("/api/apps/{app}/entities/{entity}/records");
        records.MapGet("", ListAsync);
        records.MapGet("/{id}", GetAsync);
        records.MapGet("/{id}/history", HistoryAsync);
        records.MapPost("", CreateAsync);
        records.MapPatch("/{id}", UpdateAsync);
        records.MapDelete("/{id}", DeleteAsync);
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
            return Grants.SignInRequired();
        }

        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return Grants.Forbidden();
        }

        var fields = RecordFieldAccess.Read(application!, user.Roles, model);
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);

        // A field the user may not read is no sort, and its display field no search, so neither
        // can probe a hidden value.
        RecordSort? order = null;
        if (sort is not null && (!RecordSort.TryParse(sort, model, out order) || !fields.CanRead(order.Field.Name)))
        {
            errors["sort"] = ["Must be a declared field name, optionally preceded by '-'."];
        }

        if (request.Query["search"].Count > 1)
        {
            errors["search"] = ["Must be given at most once."];
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
            else if (search.Length > MaxSearchLength || search.Contains('\0', StringComparison.Ordinal))
            {
                errors["search"] = [$"Must be at most {MaxSearchLength} characters with no U+0000."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application!, model, Expressions(filters), user.Id)
            : null;
        if (await RecordQueries.ListAsync(connection, model, pageNumber, size, order, search, condition, cancellationToken) is not { } result)
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
            return Grants.SignInRequired();
        }

        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
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
        if (read.Filters is { } filters
            && await MatchAsync(connection, null, application!, model, recordId, filters, user, loggerFactory, cancellationToken) is not { Found: true, Matched: true })
        {
            return RecordNotFound();
        }

        return await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken) is { } record
            ? Results.Ok(RecordFieldAccess.Read(application!, user.Roles, model).Visible(record))
            : RecordNotFound();
    }

    // Every audit record that names the record is listed, whoever wrote it: the record API, a
    // process start, a step or a task decision. The actor's display name is the one stored with
    // the audit record, so it is null for system, anonymous and audit records written before
    // names were stored.
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
            return Grants.SignInRequired();
        }

        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
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

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (!PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size))
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var readable = read.Filters is { } filters
            ? await MatchAsync(connection, null, application!, model, recordId, filters, user, loggerFactory, cancellationToken) is { Found: true, Matched: true }
            : await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken) is not null;
        if (!readable)
        {
            return RecordNotFound();
        }

        var fields = RecordFieldAccess.Read(application!, user.Roles, model);
        var result = await AuditRecords.ListForRecordAsync(connection, model.Id, recordId, pageNumber, size, cancellationToken);
        var items = result.Items
            .Select(item => new RecordHistoryItem(
                item.Id,
                item.OccurredAt.UtcDateTime,
                item.Actor,
                item.ActorName,
                item.Action,
                item.ProcessInstanceId,
                VisibleDetails(JsonNode.Parse(item.Details)!, fields)))
            .ToList();
        return Results.Ok(new RecordHistoryResponse(items, pageNumber, size, result.TotalCount));
    }

    // The body is read here rather than bound, so the parser reports every body problem by JSON
    // Pointer and the content type is checked before any of the body is read.
    private static async Task<IResult> CreateAsync(
        string app,
        string entity,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var access = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Create);
        if (!access.Granted)
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var parsed = RecordInputParser.Parse(await ReadBodyAsync(request, cancellationToken), model, application!, RecordOperation.Create);
        if (parsed.Errors is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        if (RecordFieldAccess.Unwritable(parsed.Input!, PolicyEvaluator.Fields(application!, user.Roles, model, PolicyAction.Create)) is { } unwritable)
        {
            return Grants.UnwritableFields(unwritable);
        }

        // The creator fields are set before the computed fields and validations, so they see the
        // record as it will be stored.
        var input = parsed.Input!;
        var values = RecordCreator.WithCreator(model, input.Values, user.Id);
        var computed = RecordComputer.ComputeCreate(application!, model, values, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Results.ValidationProblem(computeErrors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        RecordWriteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // The validations run in the write transaction, so their now() is its start time.
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            if (RecordValidator.ValidateCreate(application!, model, computed.Values, computed.Rows, now) is { } failures)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.ValidationProblem(failures);
            }

            result = await RecordCommands.CreateAsync(connection, application!, model, computed.Values, computed.Rows, cancellationToken: cancellationToken);
            var written = result is { Outcome: RecordWriteOutcome.Written, Record: { } record } ? record : null;
            var outside = written is not null
                && !await WithinFiltersAsync(connection, transaction, application!, model, written.Id, access, user, loggerFactory, cancellationToken);
            await CompleteAsync(
                transaction,
                written is not null && !outside
                    ? RecordAudit(user, AuditActions.RecordCreated, application!, model, written.Id, written.Version)
                    : null,
                cancellationToken);
            if (outside)
            {
                return Grants.OutsideFilter();
            }
        }

        // The location uses the model's names, so a record has one URL whatever case the caller used.
        return result is { Outcome: RecordWriteOutcome.Written, Record: { } created }
            ? Results.Created(
                $"/api/apps/{application!.Manifest.Name}/entities/{model.Name}/records/{created.Id:D}",
                RecordFieldAccess.Read(application, user.Roles, model).Visible(created))
            : WriteFailure(result);
    }

    private static async Task<IResult> UpdateAsync(
        string app,
        string entity,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var access = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Update);
        if (!access.Granted)
        {
            return Grants.Forbidden();
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        // An update needs the stored record to be readable, then to match an update filter. Each
        // check gives the version it saw, which the body must name, so a write in between cannot
        // carry the record past them.
        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var checkedVersions = new List<long>();
        if (read.Filters is { } readFilters)
        {
            var match = await MatchAsync(connection, null, application!, model, recordId, readFilters, user, loggerFactory, cancellationToken);
            if (match is not { Found: true, Matched: true })
            {
                return RecordNotFound();
            }

            checkedVersions.Add(match.Version);
        }

        if (access.Filters is { } updateFilters)
        {
            var match = await MatchAsync(connection, null, application!, model, recordId, updateFilters, user, loggerFactory, cancellationToken);
            if (!match.Found)
            {
                return RecordNotFound();
            }

            if (!match.Matched)
            {
                return Grants.OutsideFilter();
            }

            checkedVersions.Add(match.Version);
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var parsed = RecordInputParser.Parse(await ReadBodyAsync(request, cancellationToken), model, application!, RecordOperation.Update);
        if (parsed.Errors is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        if (RecordFieldAccess.Unwritable(parsed.Input!, PolicyEvaluator.Fields(application!, user.Roles, model, PolicyAction.Update)) is { } unwritable)
        {
            return Grants.UnwritableFields(unwritable);
        }

        var input = parsed.Input!;
        if (checkedVersions.Any(version => version != input.Version))
        {
            return WriteFailure(new RecordWriteResult(RecordWriteOutcome.StaleVersion));
        }

        // The stored record is read outside the write transaction. The client read its version
        // earlier, so a write that passes the version check finds the record as computed here and
        // validated below.
        Record? stored = null;
        if (model.Validations.Count > 0 || model.HasComputedFields)
        {
            stored = await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken);
            if (stored is null)
            {
                return RecordNotFound();
            }
        }

        var computed = RecordComputer.ComputeUpdate(application!, model, stored, input.Values, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Results.ValidationProblem(computeErrors);
        }

        RecordWriteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // The validations run in the write transaction, so their now() is its start time.
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            if (RecordValidator.ValidateUpdate(application!, model, stored, computed.Values, computed.Rows, now) is { } failures)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.ValidationProblem(failures);
            }

            result = await RecordCommands.UpdateAsync(connection, application!, model, recordId, input.Version!.Value, computed.Values, computed.Rows, cancellationToken);
            var written = result is { Outcome: RecordWriteOutcome.Written, Record: { } record } ? record : null;
            var outside = written is not null
                && !await WithinFiltersAsync(connection, transaction, application!, model, written.Id, access, user, loggerFactory, cancellationToken);
            await CompleteAsync(
                transaction,
                written is not null && !outside
                    ? RecordAudit(user, AuditActions.RecordUpdated, application!, model, written.Id, written.Version, SetFields(model, input))
                    : null,
                cancellationToken);
            if (outside)
            {
                return Grants.OutsideFilter();
            }
        }

        return result is { Outcome: RecordWriteOutcome.Written, Record: { } updated }
            ? Results.Ok(RecordFieldAccess.Read(application!, user.Roles, model).Visible(updated))
            : WriteFailure(result);
    }

    private static async Task<IResult> DeleteAsync(
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
            return Grants.SignInRequired();
        }

        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var access = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Delete);
        if (!access.Granted)
        {
            return Grants.Forbidden();
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        // A delete needs the stored record to be readable, then to match a delete filter.
        var read = PolicyEvaluator.Access(application!, user.Roles, model, PolicyAction.Read);
        if (!read.Granted)
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        RecordDeleteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // The row is locked first, so the filters see the version that is deleted.
            if (read.Filters is not null || access.Filters is not null)
            {
                if (!await RecordCommands.LockAsync(connection, model, recordId, cancellationToken)
                    || (read.Filters is { } readFilters
                        && await MatchAsync(connection, transaction, application!, model, recordId, readFilters, user, loggerFactory, cancellationToken) is not { Found: true, Matched: true }))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return RecordNotFound();
                }

                if (!await WithinFiltersAsync(connection, transaction, application!, model, recordId, access, user, loggerFactory, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Grants.OutsideFilter();
                }
            }

            result = await RecordCommands.DeleteAsync(connection, model, recordId, cancellationToken);
            await CompleteAsync(
                transaction,
                result is { Outcome: RecordDeleteOutcome.Deleted, Version: { } version }
                    ? RecordAudit(user, AuditActions.RecordDeleted, application!, model, recordId, version)
                    : null,
                cancellationToken);
        }

        return result.Outcome switch
        {
            RecordDeleteOutcome.Deleted => Results.NoContent(),
            RecordDeleteOutcome.NotFound => RecordNotFound(),
            // Names no table or constraint: the referencing entity is not part of the response.
            RecordDeleteOutcome.Referenced => Conflict("Another record references this record."),
            var outcome => throw new InvalidOperationException($"Unexpected delete outcome {outcome}."),
        };
    }

    /// <summary>
    /// Whether the record with <paramref name="recordId"/> matches a filter of
    /// <paramref name="access"/>, or <see langword="true"/> when it reaches every record. Run it
    /// inside <paramref name="transaction"/> to check a record as the transaction wrote it.
    /// </summary>
    private static async Task<bool> WithinFiltersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        RecordAccess access,
        CurrentUserResponse user,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
        access.Filters is not { } filters
        || (await MatchAsync(connection, transaction, application, model, recordId, filters, user, loggerFactory, cancellationToken)).Matched;

    /// <summary>
    /// Checks the record with <paramref name="recordId"/> against <paramref name="filters"/> for
    /// <paramref name="user"/>, and logs the policy and grant of each filter that failed for it.
    /// </summary>
    private static async Task<RecordFilterMatch> MatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        IReadOnlyList<RecordFilter> filters,
        CurrentUserResponse user,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var match = await RecordFilters.MatchAsync(
            connection, transaction, application, model, recordId, Expressions(filters), user.Id, cancellationToken);
        if (match.Failed.Count > 0)
        {
            var logger = Logger(loggerFactory);
            foreach (var index in match.Failed)
            {
                RecordFilterLog.FilterFailed(logger, filters[index].Policy, filters[index].Grant, model.Name);
            }
        }

        return match;
    }

    private static List<ExpressionModel> Expressions(IReadOnlyList<RecordFilter> filters) =>
        [.. filters.Select(filter => filter.Expression)];

    private static ILogger Logger(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger(typeof(RecordEndpoints).FullName!);

    /// <summary>
    /// Finds the application and the entity in its active release, or the 404 that says which is
    /// missing. A child entity has no record routes: its rows are served through the owner record.
    /// </summary>
    private static async Task<(ApplicationModel? Application, EntityModel? Entity, IResult? NotFound)> ResolveEntityAsync(
        string app,
        string entity,
        ActiveApplicationResolver resolver,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(app, cancellationToken) is not { } application)
        {
            return (null, null, NotFound("No application is active under this name."));
        }

        return application.TryGetEntity(entity, out var model) && !application.IsChildEntity(model)
            ? (application, model, null)
            : (null, null, NotFound("The application has no entity with this name."));
    }

    /// <summary>
    /// Appends <paramref name="audit"/> and commits the write when it succeeded, which is when
    /// there is an audit record, and rolls back every write otherwise. Disposing the transaction
    /// rolls back on an exception, so a write whose audit record fails is not committed.
    /// </summary>
    private static async Task CompleteAsync(NpgsqlTransaction transaction, AuditEntry? audit, CancellationToken cancellationToken)
    {
        if (audit is not null)
        {
            await AuditRecords.AppendAsync(transaction, audit, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The audit record of a record write. Its details hold the record's version, and on update
    /// the names of the fields the body sets. They never hold a field value. Its actor is
    /// <paramref name="user"/>, with their display name.
    /// </summary>
    private static AuditEntry RecordAudit(
        CurrentUserResponse user,
        string action,
        ApplicationModel application,
        EntityModel model,
        Guid recordId,
        long version,
        IEnumerable<string>? fields = null)
    {
        var details = new JsonObject { ["version"] = version };
        if (fields is not null)
        {
            details["fields"] = new JsonArray([.. fields.Select(name => JsonValue.Create(name))]);
        }

        return new AuditEntry(
            user.Id,
            action,
            application.Manifest.Id,
            model.Id,
            recordId,
            ProcessInstanceId: null,
            details,
            user.DisplayName);
    }

    /// <summary>
    /// The declared names of the fields and child collections an update body sets, in declaration
    /// order. A field sent with its stored value is listed too, and a computed field never is.
    /// </summary>
    internal static IEnumerable<string> SetFields(EntityModel model, RecordInput input)
    {
        var named = input.Values.Select(value => value.Field.Name)
            .Concat(input.Rows.Select(rows => rows.Collection.Name))
            .ToHashSet(StringComparer.Ordinal);
        return model.Fields.Select(field => field.Name).Where(named.Contains);
    }

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

    internal static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        return body.ToArray();
    }

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

/// <summary>One page of a record list and the number of records that match the search, or of every record without one.</summary>
internal sealed record RecordListResponse(IReadOnlyList<Record> Items, int Page, int PageSize, long TotalCount);

/// <summary>One page of a record's audit records, newest first, and the number of its audit records.</summary>
internal sealed record RecordHistoryResponse(IReadOnlyList<RecordHistoryItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// One audit record of a record. <see cref="ActorName"/> is the actor's display name as stored with
/// the audit record, or null for <c>system</c>, <c>anonymous</c> and audit records written before
/// names were stored.
/// </summary>
internal sealed record RecordHistoryItem(
    Guid Id,
    DateTime OccurredAt,
    string Actor,
    string? ActorName,
    string Action,
    Guid? ProcessInstanceId,
    JsonNode Details);
