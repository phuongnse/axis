using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.FormBuilder.Application.Repositories;
using Axis.DataModeling.Application.Commands.AddField;
using Axis.DataModeling.Application.Commands.CreateModel;
using Axis.DataModeling.Application.Commands.DeleteModel;
using Axis.DataModeling.Application.Commands.RemoveField;
using Axis.DataModeling.Application.Commands.ReorderFields;
using Axis.DataModeling.Application.Commands.UpdateField;
using Axis.DataModeling.Application.Commands.UpdateModel;
using Axis.DataModeling.Application.Queries.GetModel;
using Axis.DataModeling.Application.Queries.GetModels;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class ModelEndpoints
{
    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/models")
            .RequireAuthorization();

        group.MapGet("/", GetModels)
            .RequireAuthorization(Permissions.DataModeling.ModelRead)
            .WithName("GetModels")
            .WithSummary("List all data models for the organization")
            .WithTags("DataModeling")
            .Produces<IReadOnlyList<ModelSummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("/", CreateModel)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite)
            .WithName("CreateModel")
            .WithSummary("Create a new data model")
            .WithTags("DataModeling")
            .Produces<object>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409);

        group.MapGet("/{modelId:guid}", GetModel)
            .RequireAuthorization(Permissions.DataModeling.ModelRead)
            .WithName("GetModel")
            .WithSummary("Get a data model by ID")
            .WithTags("DataModeling")
            .Produces<ModelDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{modelId:guid}", UpdateModel)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite)
            .WithName("UpdateModel")
            .WithSummary("Update a data model")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapDelete("/{modelId:guid}", DeleteModel)
            .RequireAuthorization(Permissions.DataModeling.ModelDelete)
            .WithName("DeleteModel")
            .WithSummary("Soft-delete a data model")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // Field management
        group.MapPost("/{modelId:guid}/fields", AddField)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite)
            .WithName("AddFieldToModel")
            .WithSummary("Add a field to a data model")
            .WithTags("DataModeling")
            .Produces<object>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{modelId:guid}/fields/{fieldId:guid}", UpdateField)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite)
            .WithName("UpdateFieldInModel")
            .WithSummary("Update a field in a data model")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapDelete("/{modelId:guid}/fields/{fieldId:guid}", RemoveField)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite)
            .WithName("RemoveFieldFromModel")
            .WithSummary("Remove a field from a data model")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{modelId:guid}/fields/order", ReorderFields)
            .RequireAuthorization(Permissions.DataModeling.ModelWrite)
            .WithName("ReorderModelFields")
            .WithSummary("Reorder fields in a data model")
            .WithTags("DataModeling")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> GetModels(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        PagedResult<ModelSummaryDto> result = await mediator.Send(new GetModelsQuery(currentUser.OrgId, page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateModel(
        [FromBody] CreateModelRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new CreateModelCommand(
            request.Name,
            request.Description,
            request.Icon,
            request.Color,
            currentUser.OrgId,
            currentUser.UserId.ToString()), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/models/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> GetModel(
        Guid modelId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ModelDetailDto> result = await mediator.Send(new GetModelQuery(modelId, currentUser.OrgId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Ok(result.Value);
    }

    private static async Task<IResult> UpdateModel(
        Guid modelId,
        [FromBody] UpdateModelRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new UpdateModelCommand(
            modelId,
            currentUser.OrgId,
            request.Name,
            request.Description,
            request.Icon,
            request.Color), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteModel(
        Guid modelId,
        CurrentUser currentUser,
        ISender mediator,
        IFormModelReferenceRepository formModelReferences,
        CancellationToken ct)
    {
        int formReferenceCount = await formModelReferences.CountActiveReferencesToModelAsync(
            modelId, currentUser.OrgId, ct);
        if (formReferenceCount > 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail:
                $"This model is used by {formReferenceCount} form(s). Remove those references before deleting.");
        }

        Result result = await mediator.Send(new DeleteModelCommand(modelId, currentUser.OrgId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> AddField(
        Guid modelId,
        [FromBody] AddFieldRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new AddFieldCommand(
            modelId,
            currentUser.OrgId,
            request.Name,
            request.Label,
            request.Type,
            request.IsRequired,
            request.Config), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/models/{modelId}/fields/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> UpdateField(
        Guid modelId,
        Guid fieldId,
        [FromBody] UpdateFieldRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new UpdateFieldCommand(
            modelId,
            fieldId,
            currentUser.OrgId,
            request.Label,
            request.HelpText,
            request.IsRequired,
            request.Config), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveField(
        Guid modelId,
        Guid fieldId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new RemoveFieldCommand(modelId, fieldId, currentUser.OrgId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderFields(
        Guid modelId,
        [FromBody] ReorderFieldsRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ReorderFieldsCommand(modelId, currentUser.OrgId, request.FieldIds), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}
