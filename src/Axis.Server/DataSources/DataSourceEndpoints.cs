using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Exports;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Npgsql;

namespace Axis.Server.DataSources;

/// <summary>
/// The read endpoint of a data source in the active release of an application. Only <c>GET</c>
/// is routed. A request is checked in order: session or service account bearer token (401), then the path (404), then the user's
/// grants (403), then the user's read filters (409), and only then the query. A request without a
/// session or a valid bearer token is a 401 before the path is read. A path that names no active application or data source is a 404. Reading the rows takes
/// a <c>read</c> grant on the data source, a <c>read</c> grant on its root entity, and a grant to
/// read every field along the path of every projected field, as <see cref="DataSourceFieldAccess"/>
/// decides. The rows, the count and the measures cover only the root records that match one of
/// the user's read filters on the root entity, as well as the data source's filter. The paging
/// and sorting rules are those of the record list, except that the default page size and sort are
/// the data source's own. A data source with <c>search</c> takes the record list's <c>search</c> and keeps
/// only the root records where a search field contains it. Without that option a given
/// <c>search</c> is a 400. A request is a 409 when a read filter of the user reads a computed field that
/// is being recomputed, with the policy title and no <c>errors</c>. After the query is valid, it is a 409
/// with <c>errors</c> keyed <c>sort</c>, <c>filter</c>, <c>aggregate</c> or <c>search</c> when the sort, the filter, a
/// group field, a measure or a searched field is such a field. A projected field shows its stored value. The data
/// source parameters are read by <see cref="DataSourceParameterReader"/>. A database error while evaluating the filter or the
/// read filters is a 400 problem with a fixed title, and each read filter is logged by its policy
/// and grant. The problem titles never contain text from the request, SQL or storage names.
/// The export route takes the same <c>search</c> and checks it the same way. A data source of a module is written <c>{module id}/{data source name}</c>, as two path segments,
/// for example <c>data-sources/departments/Departments/rows</c>. It has the same checks as any
/// other data source, and only that form reaches it.
/// </summary>
internal static class DataSourceEndpoints
{
    private const int DefaultPage = 1;

    private const int MaxPageSize = 100;

    public static void MapDataSourceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // A data source of the application has one segment, and a data source of a module has the
        // module id and the data source name as two, so no data source has two URLs.
        foreach (var pattern in new[] { "/api/apps/{app}/data-sources/{dataSource}/rows", "/api/apps/{app}/data-sources/{module}/{dataSource}/rows" })
        {
            var rows = endpoints.MapGroup(pattern).WithMetadata(ServiceAccountRoute.Instance);
            rows.MapGet("", ListAsync).WithQueryTimeout(QueryTimeoutSettings.Interactive);
        }

        // Only a signed-in user exports, so a service account never reaches it.
        foreach (var pattern in new[] { "/api/apps/{app}/data-sources/{dataSource}/export", "/api/apps/{app}/data-sources/{module}/{dataSource}/export" })
        {
            endpoints.MapGet(pattern, ExportAsync).WithQueryTimeout(QueryTimeoutSettings.Export);
        }
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
        string? search,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(context) is not { } user)
        {
            return Grants.SignInRequired(context);
        }

        if (await resolver.ResolveAsync(app, cancellationToken) is not { } application)
        {
            return NotFound("No application is active under this name.");
        }

        if (!application.TryGetDataSource(DataSourceName(context, dataSource), out var model))
        {
            return NotFound("The application has no data source with this name.");
        }

        if (!PolicyEvaluator.AllowsRead(application, user.RolesIn(application), model)
            || DataSourceFieldAccess.Read(application, user.RolesIn(application), model) is not { } fields)
        {
            return Grants.Forbidden();
        }

        // AllowsRead found the root entity, so it is in the model.
        var root = application.FindEntity(model.Entity.Id)!;
        var read = PolicyEvaluator.Access(application, user.RolesIn(application), root, PolicyAction.Read);
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application, root, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
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
            errors["sort"] = ["Must be a projected field name that is not a reference or sensitive, or a group field or measure name when the data source is grouped, optionally preceded by '-'."];
        }

        var searching = ReadSearch(context, model, search, errors);
        var parameters = DataSourceParameterReader.Read(context.Request.QueryString, model, errors);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // A sort, a group field, a measure, a filter or a search on a field that is being recomputed would decide from a stale stored value.
        var pending = await DataSourcePendingFields.ReadAsync(connection, database.ReadTransaction, application, model, order, searching, cancellationToken);
        if (pending.Count > 0)
        {
            return Results.ValidationProblem(pending.ToDictionary(), statusCode: StatusCodes.Status409Conflict, title: "A field is being recomputed.");
        }

        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application, root, [.. filters.Select(filter => filter.Expression)], user.Id)
            : null;
        var result = await DataSourceQueries.ListAsync(
            connection, database.ReadTransaction, application, model, pageNumber, size, order, parameters, condition, search, cancellationToken);
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

    // An export takes the query of the rows route, its search included, without its paging. The grants, the filters and the
    // field checks are the rows route's, and the export grant is checked in place of the read grant.
    // The rows are written to a temporary file, so the file is complete before the audit record and
    // before any byte is sent. A grouped data source writes its group fields, then its measures.
    private static async Task<IResult> ExportAsync(
        HttpContext context,
        string app,
        string dataSource,
        string? sort,
        string? search,
        string? format,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ExportSettings export,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(context) is not { } user)
        {
            return Grants.SignInRequired(context);
        }

        if (await resolver.ResolveAsync(app, cancellationToken) is not { } application)
        {
            return NotFound("No application is active under this name.");
        }

        if (!application.TryGetDataSource(DataSourceName(context, dataSource), out var model))
        {
            return NotFound("The application has no data source with this name.");
        }

        if (!PolicyEvaluator.AllowsExport(application, user.RolesIn(application), model)
            || DataSourceFieldAccess.Read(application, user.RolesIn(application), model) is not { } fields)
        {
            return Grants.Forbidden();
        }

        // AllowsExport found the root entity, so it is in the model.
        var root = application.FindEntity(model.Entity.Id)!;
        var read = PolicyEvaluator.Access(application, user.RolesIn(application), root, PolicyAction.Read);
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application, root, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var order = DataSourceSort.Default(model);
        if (!string.IsNullOrEmpty(sort) && !DataSourceSort.TryParse(sort, model, out order))
        {
            errors["sort"] = ["Must be a projected field name that is not a reference or sensitive, or a group field or measure name when the data source is grouped, optionally preceded by '-'."];
        }

        var searching = ReadSearch(context, model, search, errors);
        var parameters = DataSourceParameterReader.Read(context.Request.QueryString, model, errors);
        ExportFile.TryParseFormat(format, errors, out var exportFormat);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // A sort, a group field, a measure, a filter or a search on a field that is being recomputed would decide from a stale stored value.
        var pending = await DataSourcePendingFields.ReadAsync(connection, database.ReadTransaction, application, model, order, searching, cancellationToken);
        if (pending.Count > 0)
        {
            return Results.ValidationProblem(pending.ToDictionary(), statusCode: StatusCodes.Status409Conflict, title: "A field is being recomputed.");
        }

        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application, root, [.. filters.Select(filter => filter.Expression)], user.Id)
            : null;
        var columns = ExportColumns(model);
        string?[] Cells(DataSourceRow row)
        {
            var visible = fields.Visible(row);
            return [.. columns.Select(column => ExportValues.Format(
                column.Type,
                visible.Values.GetValueOrDefault(column.Name),
                visible.Labels.TryGetValue(column.Name, out var label) ? JsonValue.Create(label) : null))];
        }

        try
        {
            return await ExportFile.RunAsync(
                context,
                database,
                connection,
                exportFormat,
                model.Name,
                columns,
                DataSourceQueries.ReadAllAsync(connection, application, model, order, parameters, condition, search, export.MaxRows + 1, cancellationToken),
                Cells,
                export.MaxRows,
                (rows, truncated) => new AuditEntry(
                    user.Id,
                    AuditActions.RecordsExported,
                    application.Manifest.Id,
                    null,
                    null,
                    null,
                    new JsonObject
                    {
                        ["dataSourceId"] = model.Id,
                        ["format"] = exportFormat.Name(),
                        ["rowCount"] = rows,
                        ["truncated"] = truncated,
                    },
                    user.DisplayName),
                cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState.StartsWith("22", StringComparison.Ordinal))
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
    }

    // Without the option, only a given search is refused, and an empty one is no search. Returns
    // whether the request searches, and adds what is wrong with the search to errors.
    private static bool ReadSearch(HttpContext context, DataSourceModel model, string? search, SortedDictionary<string, string[]> errors)
    {
        var searching = !string.IsNullOrEmpty(search);
        if (SearchQuery.IsRepeated(context.Request))
        {
            errors["search"] = model.Search ? SearchQuery.RepeatedError : ["The data source does not take a search."];
        }
        else if (searching && !model.Search)
        {
            errors["search"] = ["The data source does not take a search."];
        }
        else if (searching && SearchQuery.InvalidError(search!) is { } invalid)
        {
            errors["search"] = invalid;
        }

        return searching;
    }

    // The columns of a row in the order the row holds them. A count or a count of distinct values is an
    // integer, a sum or an average a decimal, and a minimum or maximum has the type of its field.
    private static List<ExportColumn> ExportColumns(DataSourceModel model) =>
        model.Aggregate is { } aggregate
            ?
            [
                .. aggregate.GroupBy.Select(field => new ExportColumn(field.Name, field.ValueType)),
                .. aggregate.Measures.Select(measure => new ExportColumn(
                    measure.Name,
                    measure.Function switch
                    {
                        AggregateFunction.Count or AggregateFunction.CountDistinct => FieldType.Integer,
                        AggregateFunction.Sum or AggregateFunction.Avg => FieldType.Decimal,
                        _ => measure.Field!.Field.Type,
                    })),
            ]
            : [.. model.Fields.Select(field => new ExportColumn(field.Name, field.ValueType))];

    // The route of a module data source has a module segment before the data source name.
    private static string DataSourceName(HttpContext context, string dataSource) =>
        context.GetRouteValue("module") is string module ? $"{module}/{dataSource}" : dataSource;

    private static bool TryParseInteger(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of data source rows and the number of rows that pass the filter, or <see langword="null"/> when the count ran past the query timeout.</summary>
internal sealed record DataSourceRowsResponse(IReadOnlyList<DataSourceRow> Items, int Page, int PageSize, long? TotalCount);
