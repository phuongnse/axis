using System.Globalization;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.DataSources;

/// <summary>
/// The read endpoint of a data source in the active release of an application. Only <c>GET</c>
/// is routed. A request is checked in order: session (401), then the path (404), then the user's
/// grants (403), and only then the query. A request without a session is a 401 before the path is
/// read. A path that names no active application or data source is a 404. Reading the rows takes
/// a <c>read</c> grant on the data source, a <c>read</c> grant on its root entity, and a grant to
/// read every field along the path of every projected field, as <see cref="DataSourceFieldAccess"/>
/// decides. The rows, the count and the measures cover only the root records that match one of
/// the user's read filters on the root entity, as well as the data source's filter. The paging
/// and sorting rules are those of the record list, except that the default page size and sort are
/// the data source's own. The data source parameters are read by
/// <see cref="DataSourceParameterReader"/>. A database error while evaluating the filter or the
/// read filters is a 400 problem with a fixed title, and each read filter is logged by its policy
/// and grant. The problem titles never contain text from the request, SQL or storage names.
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
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(context) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveAsync(app, cancellationToken) is not { } application)
        {
            return NotFound("No application is active under this name.");
        }

        if (!application.TryGetDataSource(dataSource, out var model))
        {
            return NotFound("The application has no data source with this name.");
        }

        if (!PolicyEvaluator.AllowsRead(application, user.Roles, model)
            || DataSourceFieldAccess.Read(application, user.Roles, model) is not { } fields)
        {
            return Grants.Forbidden();
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
            errors["sort"] = ["Must be a projected field name that is not a reference, or a group field or measure name when the data source is grouped, optionally preceded by '-'."];
        }

        var parameters = DataSourceParameterReader.Read(context.Request.QueryString, model, errors);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // AllowsRead found the root entity, so it is in the model.
        var root = application.FindEntity(model.Entity.Id)!;
        var read = PolicyEvaluator.Access(application, user.Roles, root, PolicyAction.Read);
        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application, root, [.. filters.Select(filter => filter.Expression)], user.Id)
            : null;
        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await DataSourceQueries.ListAsync(
            connection, application, model, pageNumber, size, order, parameters, condition, cancellationToken);
        if (result is null)
        {
            // PostgreSQL cannot say which part of the WHERE clause failed, so every read filter is named.
            if (read.Filters is not null)
            {
                var logger = loggerFactory.CreateLogger(typeof(DataSourceEndpoints).FullName!);
                foreach (var filter in read.Filters)
                {
                    RecordFilterLog.DataSourceFailed(logger, filter.Policy, filter.Grant, model.Name);
                }
            }

            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The data source filter could not be evaluated for these rows.");
        }

        return Results.Ok(new DataSourceRowsResponse([.. result.Items.Select(fields.Visible)], pageNumber, size, result.TotalCount));
    }

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of data source rows and the number of rows that pass the filter.</summary>
internal sealed record DataSourceRowsResponse(IReadOnlyList<DataSourceRow> Items, int Page, int PageSize, long TotalCount);
