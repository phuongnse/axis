using System.Globalization;
using Axis.Data.DataSources;
using Axis.Server.Applications;
using Axis.Server.Tenancy;

namespace Axis.Server.DataSources;

/// <summary>
/// The read endpoint of a data source in the active release of an application. Only <c>GET</c>
/// is routed. A path that names no active application or data source is a 404 before the query
/// is checked. The paging and sorting rules are those of the record list, except that the default
/// page size and sort are the data source's own. The data source parameters are read by
/// <see cref="DataSourceParameterReader"/>. A database error while evaluating the filter is a
/// 400 problem with a fixed title. The problem titles never contain text from the request, SQL or
/// storage names.
/// </summary>
internal static class DataSourceEndpoints
{
    private const int DefaultPage = 1;

    private const int MaxPageSize = 100;

    public static void MapDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var rows = endpoints.MapGroup("/api/apps/{app}/data-sources/{dataSource}/rows");
        rows.MapGet("", ListAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    // A repeated parameter binds as its values joined by commas, which never parses. An empty
    // value means the parameter was not given. The data source parameters are read from the raw
    // query string instead, because their names match exactly.
    private static async Task<IResult> ListAsync(
        HttpContext context,
        string app,
        string dataSource,
        string? page,
        string? pageSize,
        string? sort,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await resolver.ResolveAsync(app, cancellationToken) is not { } application)
        {
            return NotFound("No application is active under this name.");
        }

        if (!application.TryGetDataSource(dataSource, out var model))
        {
            return NotFound("The application has no data source with this name.");
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var pageNumber = DefaultPage;
        if (!string.IsNullOrEmpty(page) && (!TryParseInteger(page, out pageNumber) || pageNumber < 1))
        {
            errors["page"] = ["Must be an integer of at least 1."];
        }

        var size = model.PageSize;
        if (!string.IsNullOrEmpty(pageSize) && (!TryParseInteger(pageSize, out size) || size is < 1 or > MaxPageSize))
        {
            errors["pageSize"] = [$"Must be an integer from 1 to {MaxPageSize}."];
        }

        var order = DataSourceSort.Default(model);
        if (!string.IsNullOrEmpty(sort) && !DataSourceSort.TryParse(sort, model, out order))
        {
            errors["sort"] = ["Must be a projected field name that is not a reference, optionally preceded by '-'."];
        }

        var parameters = DataSourceParameterReader.Read(context.Request.QueryString, model, errors);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await DataSourceQueries.ListAsync(connection, application, model, pageNumber, size, order, parameters, cancellationToken);
        if (result is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The data source filter could not be evaluated for these rows.");
        }

        return Results.Ok(new DataSourceRowsResponse(result.Items, pageNumber, size, result.TotalCount));
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of data source rows and the number of rows that pass the filter.</summary>
internal sealed record DataSourceRowsResponse(IReadOnlyList<DataSourceRow> Items, int Page, int PageSize, long TotalCount);
