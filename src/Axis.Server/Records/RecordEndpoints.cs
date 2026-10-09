using System.Globalization;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Server.Applications;
using Axis.Server.Tenancy;
using Microsoft.Net.Http.Headers;
using Npgsql;

namespace Axis.Server.Records;

/// <summary>
/// Endpoints for the records of an entity in the active release of an application. A path that
/// names no active application, entity or record is a 404 before the query or body is checked; a
/// child entity is no entity here. A create or update writes the record and its child rows in one
/// transaction.
/// A body is checked in order: content type (415), then body (400), then the computed fields of
/// the record as it will be stored and of its rows (400), then the entity's validations on that
/// record (400), then storage (404, 409). An update of an entity with computed fields or
/// validations reads the stored record first, so an unknown record is a 404 there.
/// A delete is 204, or 404 or 409 from storage. The problem titles never contain text from the
/// request.
/// </summary>
internal static class RecordEndpoints
{
    private const int DefaultPage = 1;

    private const int DefaultPageSize = 20;

    private const int MaxPageSize = 100;

    public static void MapRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var records = endpoints.MapGroup("/api/apps/{app}/entities/{entity}/records");
        records.MapGet("", ListAsync);
        records.MapGet("/{id}", GetAsync);
        records.MapPost("", CreateAsync);
        records.MapPatch("/{id}", UpdateAsync);
        records.MapDelete("/{id}", DeleteAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    // A repeated parameter binds as its values joined by commas, which never parses.
    private static async Task<IResult> ListAsync(
        string app,
        string entity,
        string? page,
        string? pageSize,
        string? sort,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var (_, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var pageNumber = DefaultPage;
        if (page is not null && (!TryParseInteger(page, out pageNumber) || pageNumber < 1))
        {
            errors["page"] = ["Must be an integer of at least 1."];
        }

        var size = DefaultPageSize;
        if (pageSize is not null && (!TryParseInteger(pageSize, out size) || size is < 1 or > MaxPageSize))
        {
            errors["pageSize"] = [$"Must be an integer from 1 to {MaxPageSize}."];
        }

        RecordSort? order = null;
        if (sort is not null && !RecordSort.TryParse(sort, model, out order))
        {
            errors["sort"] = ["Must be a declared field name, optionally preceded by '-'."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await RecordQueries.ListAsync(connection, model, pageNumber, size, order, cancellationToken);
        return Results.Ok(new RecordListResponse(result.Items, pageNumber, size, result.TotalCount));
    }

    private static async Task<IResult> GetAsync(
        string app,
        string entity,
        string id,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken) is { } record
            ? Results.Ok(record)
            : RecordNotFound();
    }

    // The body is read here rather than bound, so the parser reports every body problem by JSON
    // Pointer and the content type is checked before any of the body is read.
    private static async Task<IResult> CreateAsync(
        string app,
        string entity,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        if (!IsJson(request))
        {
            return UnsupportedMediaType();
        }

        var parsed = RecordInputParser.Parse(await ReadBodyAsync(request, cancellationToken), model, application!, RecordOperation.Create);
        if (parsed.Errors is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        var input = parsed.Input!;
        var computed = RecordComputer.ComputeCreate(model, input.Values, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Results.ValidationProblem(computeErrors);
        }

        if (RecordValidator.ValidateCreate(model, computed.Values, computed.Rows) is { } failures)
        {
            return Results.ValidationProblem(failures);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        RecordWriteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            result = await RecordCommands.CreateAsync(connection, application!, model, computed.Values, computed.Rows, cancellationToken: cancellationToken);
            await CompleteAsync(transaction, result, cancellationToken);
        }

        // The location uses the model's names, so a record has one URL whatever case the caller used.
        return result is { Outcome: RecordWriteOutcome.Written, Record: { } record }
            ? Results.Created($"/api/apps/{application!.Manifest.Name}/entities/{model.Name}/records/{record.Id:D}", record)
            : WriteFailure(result);
    }

    private static async Task<IResult> UpdateAsync(
        string app,
        string entity,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var (application, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        if (!IsJson(request))
        {
            return UnsupportedMediaType();
        }

        var parsed = RecordInputParser.Parse(await ReadBodyAsync(request, cancellationToken), model, application!, RecordOperation.Update);
        if (parsed.Errors is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        var input = parsed.Input!;
        var connection = await database.GetConnectionAsync(cancellationToken);

        // The stored record is read outside the write transaction. The client read its version
        // earlier, so a write that passes the version check finds the record as computed and
        // validated here.
        Record? stored = null;
        if (model.Validations.Count > 0 || model.HasComputedFields)
        {
            stored = await RecordQueries.GetAsync(connection, application!, model, recordId, cancellationToken);
            if (stored is null)
            {
                return RecordNotFound();
            }
        }

        var computed = RecordComputer.ComputeUpdate(model, stored, input.Values, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return Results.ValidationProblem(computeErrors);
        }

        if (RecordValidator.ValidateUpdate(model, stored, computed.Values, computed.Rows) is { } failures)
        {
            return Results.ValidationProblem(failures);
        }

        RecordWriteResult result;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            result = await RecordCommands.UpdateAsync(connection, application!, model, recordId, input.Version!.Value, computed.Values, computed.Rows, cancellationToken);
            await CompleteAsync(transaction, result, cancellationToken);
        }

        return result is { Outcome: RecordWriteOutcome.Written, Record: { } record }
            ? Results.Ok(record)
            : WriteFailure(result);
    }

    private static async Task<IResult> DeleteAsync(
        string app,
        string entity,
        string id,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        var (_, model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        if (!TryParseId(id, out var recordId))
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await RecordCommands.DeleteAsync(connection, model, recordId, cancellationToken) switch
        {
            RecordDeleteOutcome.Deleted => Results.NoContent(),
            RecordDeleteOutcome.NotFound => RecordNotFound(),
            // Names no table or constraint: the referencing entity is not part of the response.
            RecordDeleteOutcome.Referenced => Conflict("Another record references this record."),
            var outcome => throw new InvalidOperationException($"Unexpected delete outcome {outcome}."),
        };
    }

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
    /// Commits the owner and its rows when the record was written, and rolls back every write
    /// otherwise. Disposing the transaction rolls back on an exception.
    /// </summary>
    private static async Task CompleteAsync(NpgsqlTransaction transaction, RecordWriteResult result, CancellationToken cancellationToken)
    {
        if (result.Outcome == RecordWriteOutcome.Written)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    // Only the hyphenated form names a record; any other text is no record, not a bad request.
    private static bool TryParseId(string id, out Guid recordId) =>
        Guid.TryParseExact(id, "D", out recordId) && id.Length == 36;

    /// <summary>
    /// Whether the content type is <c>application/json</c> with at most a <c>utf-8</c> charset.
    /// A cross-site page can send other types, such as <c>text/plain</c>, without a preflight.
    /// </summary>
    private static bool IsJson(HttpRequest request) =>
        MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
        && contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        && contentType.Parameters.All(parameter =>
            parameter.Name.Equals("charset", StringComparison.OrdinalIgnoreCase)
            && HeaderUtilities.RemoveQuotes(parameter.Value).Equals("utf-8", StringComparison.OrdinalIgnoreCase));

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken);
        return body.ToArray();
    }

    private static IResult WriteFailure(RecordWriteResult result) =>
        result.Outcome switch
        {
            RecordWriteOutcome.NotFound => RecordNotFound(),
            RecordWriteOutcome.StaleVersion => Conflict("The record has changed since this version was read."),
            RecordWriteOutcome.MissingReference => Results.ValidationProblem(result.Errors!),
            RecordWriteOutcome.UniqueViolation => Results.ValidationProblem(
                result.Errors!,
                statusCode: StatusCodes.Status409Conflict,
                title: "A value must be unique."),
            // Names no column or constraint: the storage names are not part of the API.
            RecordWriteOutcome.SchemaConflict => Conflict("The stored schema does not match the active model."),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unexpected write outcome."),
        };

    private static IResult UnsupportedMediaType() =>
        Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "The request body must be application/json.");

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static IResult RecordNotFound() => NotFound("No record has this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of a record list and the number of records in the whole entity.</summary>
internal sealed record RecordListResponse(IReadOnlyList<Record> Items, int Page, int PageSize, long TotalCount);
