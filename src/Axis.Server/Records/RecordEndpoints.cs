using System.Globalization;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Server.Applications;
using Axis.Server.Tenancy;

namespace Axis.Server.Records;

/// <summary>
/// Read-only endpoints for the records of an entity in the active release of an application. A
/// path that names no active application, entity or record is a 404 before the query is checked.
/// The problem titles never contain text from the request.
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
        var (model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
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
        var (model, notFound) = await ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        // Only the hyphenated form names a record; any other text is no record, not a bad request.
        if (id.Length != 36 || !Guid.TryParseExact(id, "D", out var recordId))
        {
            return RecordNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await RecordQueries.GetAsync(connection, model, recordId, cancellationToken) is { } record
            ? Results.Ok(record)
            : RecordNotFound();
    }

    /// <summary>Finds the entity in the active release of the application, or the 404 that says which is missing.</summary>
    private static async Task<(EntityModel? Entity, IResult? NotFound)> ResolveEntityAsync(
        string app,
        string entity,
        ActiveApplicationResolver resolver,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(app, cancellationToken) is not { } application)
        {
            return (null, NotFound("No application is active under this name."));
        }

        return application.TryGetEntity(entity, out var model)
            ? (model, null)
            : (null, NotFound("The application has no entity with this name."));
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static IResult RecordNotFound() => NotFound("No record has this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of a record list and the number of records in the whole entity.</summary>
internal sealed record RecordListResponse(IReadOnlyList<Record> Items, int Page, int PageSize, long TotalCount);
