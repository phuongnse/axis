using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.DataModeling.Application.Commands.CreateRecord;
using Axis.DataModeling.Application.Commands.DeleteRecord;
using Axis.DataModeling.Application.Commands.UpdateRecord;
using Axis.DataModeling.Application.Queries.GetRecord;
using Axis.DataModeling.Application.Queries.GetRecords;
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
            .WithSummary("List records for a data model (paginated)")
            .WithTags("DataModeling")
            .Produces<RecordsPageDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPost("/", CreateRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordWrite)
            .WithName("CreateRecord")
            .WithSummary("Create a new record")
            .WithTags("DataModeling")
            .Produces<object>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

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
            .ProducesProblem(404);

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
        [FromQuery] string? search = null)
    {
        Result<RecordsPageDto> result = await mediator.Send(
            new GetRecordsQuery(modelId, currentUser.OrgId, page, pageSize, search), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateRecord(
        Guid modelId,
        [FromBody] Dictionary<string, object?> data,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(
            new CreateRecordCommand(modelId, currentUser.OrgId, data, currentUser.UserId.ToString()), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/models/{modelId}/records/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> GetRecord(
        Guid modelId,
        Guid recordId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<RecordDto> result = await mediator.Send(
            new GetRecordQuery(recordId, modelId, currentUser.OrgId), ct);
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
            new UpdateRecordCommand(recordId, modelId, currentUser.OrgId, data), ct);
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
            new DeleteRecordCommand(recordId, modelId, currentUser.OrgId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}
