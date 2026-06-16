using System.Text;
using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.DataModeling.Application.Commands.BulkDeleteRecords;
using Axis.DataModeling.Application.Commands.CreateRecord;
using Axis.DataModeling.Application.Commands.DeleteRecord;
using Axis.DataModeling.Application.Commands.UpdateRecord;
using Axis.DataModeling.Application.Queries.ExportRecordsCsv;
using Axis.DataModeling.Application.Queries.GetRecord;
using Axis.DataModeling.Application.Queries.GetRecords;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class RecordEndpoints
{
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/models/{modelId:guid}/records")
            .RequireAuthorization();

        group.MapGet("/", GetRecords)
            .RequireAuthorization(Permissions.DataModeling.RecordRead)
            .WithName("GetRecords")
            .WithSummary("List records for a data model (paginated, filterable, sortable)")
            .WithTags("DataModeling")
            .Produces<RecordsPageDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPost("/bulk-delete", BulkDeleteRecords)
            .RequireAuthorization(Permissions.DataModeling.RecordDelete)
            .WithName("BulkDeleteRecords")
            .WithSummary("Soft-delete multiple records in a single operation")
            .WithTags("DataModeling")
            .Produces<BulkDeleteResult>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapGet("/export", ExportRecordsCsv)
            .RequireAuthorization(Permissions.DataModeling.RecordRead)
            .WithName("ExportRecordsCsv")
            .WithSummary("Export records as a CSV file")
            .WithTags("DataModeling")
            .Produces(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPost("/", CreateRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordWrite)
            .WithName("CreateRecord")
            .WithSummary("Create a new record")
            .WithTags("DataModeling")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesValidationProblem();

        group.MapGet("/{recordId:guid}", GetRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordRead)
            .WithName("GetRecord")
            .WithSummary("Get a record by ID")
            .WithTags("DataModeling")
            .Produces<RecordDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{recordId:guid}", UpdateRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordWrite)
            .WithName("UpdateRecord")
            .WithSummary("Update a record")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesValidationProblem();

        group.MapDelete("/{recordId:guid}", DeleteRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordDelete)
            .WithName("DeleteRecord")
            .WithSummary("Delete a record")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> GetRecords(
        Guid modelId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery(Name = "filter")] string[]? filter = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        IResult? filterError = ParseFilters(filter, out IReadOnlyList<RecordFilter>? parsedFilters);
        if (filterError is not null) return filterError;

        Result<RecordsPageDto> result = await mediator.Send(
            new GetRecordsQuery(modelId, currentUser.TenantId, page, pageSize, search, parsedFilters, sortBy, sortDir),
            ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> BulkDeleteRecords(
        Guid modelId,
        [FromBody] BulkDeleteRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        // Coerce null (JSON null / missing property) to empty list so the handler's validation fires correctly.
        IReadOnlyList<Guid> ids = request.Ids ?? [];

        Result<BulkDeleteResult> result = await mediator.Send(
            new BulkDeleteRecordsCommand(ids, modelId, currentUser.TenantId), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> ExportRecordsCsv(
        Guid modelId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] string? search = null,
        [FromQuery(Name = "filter")] string[]? filter = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        IResult? filterError = ParseFilters(filter, out IReadOnlyList<RecordFilter>? parsedFilters);
        if (filterError is not null) return filterError;

        Result<CsvExportDto> result = await mediator.Send(
            new ExportRecordsCsvQuery(modelId, currentUser.TenantId, search, parsedFilters, sortBy, sortDir),
            ct);

        if (result.IsFailure) return result.ToProblemDetails();

        CsvExportDto csv = result.Value;
        return Results.File(
            Encoding.UTF8.GetBytes(csv.Content),
            contentType: "text/csv",
            fileDownloadName: csv.FileName);
    }

    private static async Task<IResult> CreateRecord(
        Guid modelId,
        [FromBody] Dictionary<string, object?> data,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(
            new CreateRecordCommand(modelId, currentUser.TenantId, data, currentUser.UserId.ToString()), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/models/{modelId}/records/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> GetRecord(
        Guid modelId,
        Guid recordId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<RecordDto> result = await mediator.Send(
            new GetRecordQuery(recordId, modelId, currentUser.TenantId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateRecord(
        Guid modelId,
        Guid recordId,
        [FromBody] Dictionary<string, object?> data,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new UpdateRecordCommand(recordId, modelId, currentUser.TenantId, data), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRecord(
        Guid modelId,
        Guid recordId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new DeleteRecordCommand(recordId, modelId, currentUser.TenantId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses repeated ?filter=field:op:value params.
    /// Returns a non-null IResult (HTTP 400) if any item fails TryParse; otherwise null.
    /// </summary>
    private static IResult? ParseFilters(string[]? filter, out IReadOnlyList<RecordFilter>? parsed)
    {
        parsed = null;
        if (filter is not { Length: > 0 })
            return null;

        List<RecordFilter> valid = [];
        List<string> invalid = [];

        foreach (string raw in filter)
        {
            RecordFilter? f = RecordFilter.TryParse(raw);
            if (f is not null)
                valid.Add(f);
            else
                invalid.Add(raw);
        }

        if (invalid.Count > 0)
        {
            return Result.Failure<object>(
                ErrorCodes.InvalidInput,
                $"Invalid filter syntax: {string.Join(", ", invalid)}. Expected format: field:op:value.")
                .ToProblemDetails();
        }

        parsed = valid.AsReadOnly();
        return null;
    }
}
