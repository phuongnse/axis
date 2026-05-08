using System.Text.Json;
using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.DataModeling.Application.Commands.AddFieldToDataClass;
using Axis.DataModeling.Application.Commands.CreateDataClass;
using Axis.DataModeling.Application.Commands.DeleteDataClass;
using Axis.DataModeling.Application.Commands.RemoveFieldFromDataClass;
using Axis.DataModeling.Application.Commands.UpdateDataClass;
using Axis.DataModeling.Application.Queries.GetDataClass;
using Axis.DataModeling.Application.Queries.GetDataClasses;
using Axis.DataModeling.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class DataClassEndpoints
{
    public static IEndpointRouteBuilder MapDataClassEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-classes")
            .RequireAuthorization();

        group.MapGet("/", GetDataClasses)
            .RequireAuthorization(Permissions.DataModeling.ModelRead);
        group.MapPost("/", CreateDataClass)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite);
        group.MapGet("/{dataClassId:guid}", GetDataClass)
            .RequireAuthorization(Permissions.DataModeling.ModelRead);
        group.MapPut("/{dataClassId:guid}", UpdateDataClass)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite);
        group.MapDelete("/{dataClassId:guid}", DeleteDataClass)
            .RequireAuthorization(Permissions.DataModeling.ModelDelete);

        // Field management
        group.MapPost("/{dataClassId:guid}/fields", AddField)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite);
        group.MapDelete("/{dataClassId:guid}/fields/{fieldId:guid}", RemoveField)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite);

        return app;
    }

    private static async Task<IResult> GetDataClasses(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetDataClassesQuery(currentUser.OrgId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateDataClass(
        [FromBody] CreateDataClassRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var id = await mediator.Send(new CreateDataClassCommand(
            request.Name,
            request.Description,
            currentUser.OrgId), ct);

        return Results.Created($"/api/data-classes/{id}", new { id });
    }

    private static async Task<IResult> GetDataClass(
        Guid dataClassId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetDataClassQuery(dataClassId, currentUser.OrgId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> UpdateDataClass(
        Guid dataClassId,
        [FromBody] UpdateDataClassRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new UpdateDataClassCommand(
            dataClassId,
            currentUser.OrgId,
            request.Name,
            request.Description), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteDataClass(
        Guid dataClassId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteDataClassCommand(dataClassId, currentUser.OrgId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddField(
        Guid dataClassId,
        [FromBody] AddDataClassFieldRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var config = FieldConfigHelper.Deserialize(request.Type, request.Config);
        var fieldId = await mediator.Send(new AddFieldToDataClassCommand(
            dataClassId,
            currentUser.OrgId,
            request.Name,
            request.Label,
            request.Type,
            request.IsRequired,
            config), ct);

        return Results.Created($"/api/data-classes/{dataClassId}/fields/{fieldId}", new { id = fieldId });
    }

    private static async Task<IResult> RemoveField(
        Guid dataClassId,
        Guid fieldId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new RemoveFieldFromDataClassCommand(dataClassId, fieldId, currentUser.OrgId), ct);
        return Results.NoContent();
    }
}

public record CreateDataClassRequest(string Name, string? Description);

public record UpdateDataClassRequest(string Name, string? Description);

public record AddDataClassFieldRequest(
    string Name,
    string Label,
    FieldType Type,
    bool IsRequired,
    JsonElement Config);
