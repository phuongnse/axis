using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.DataModeling.Application.Commands.CreateRecord;
using Axis.DataModeling.Application.Commands.DeleteRecord;
using Axis.DataModeling.Application.Commands.UpdateRecord;
using Axis.DataModeling.Application.Queries.GetRecord;
using Axis.DataModeling.Application.Queries.GetRecords;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class RecordEndpoints
{
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/models/{modelId:guid}/records")
            .RequireAuthorization();

        group.MapGet("/", GetRecords)
            .RequireAuthorization(Permissions.DataModeling.RecordRead);
        group.MapPost("/", CreateRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordWrite);
        group.MapGet("/{recordId:guid}", GetRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordRead);
        group.MapPut("/{recordId:guid}", UpdateRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordWrite);
        group.MapDelete("/{recordId:guid}", DeleteRecord)
            .RequireAuthorization(Permissions.DataModeling.RecordDelete);

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
        var result = await mediator.Send(
            new GetRecordsQuery(modelId, currentUser.OrgId, page, pageSize, search), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateRecord(
        Guid modelId,
        [FromBody] Dictionary<string, object?> data,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var id = await mediator.Send(new CreateRecordCommand(modelId, currentUser.OrgId, data), ct);
        return Results.Created($"/api/models/{modelId}/records/{id}", new { id });
    }

    private static async Task<IResult> GetRecord(
        Guid modelId,
        Guid recordId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetRecordQuery(recordId, modelId, currentUser.OrgId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> UpdateRecord(
        Guid modelId,
        Guid recordId,
        [FromBody] Dictionary<string, object?> data,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new UpdateRecordCommand(recordId, modelId, currentUser.OrgId, data), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRecord(
        Guid modelId,
        Guid recordId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteRecordCommand(recordId, modelId, currentUser.OrgId), ct);
        return Results.NoContent();
    }
}
