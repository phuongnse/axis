using Axis.Api.Extensions;
using Axis.Objects.Application;
using Axis.Objects.Application.Commands.CreateObjectDefinitionDraft;
using Axis.Objects.Application.Commands.PublishObjectDefinition;
using Axis.Objects.Application.Commands.SaveObjectDefinitionDraft;
using Axis.Objects.Application.Queries.GetObjectDefinition;
using Axis.Objects.Application.Queries.ListObjectDefinitions;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class ObjectDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapObjectDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/object-definitions")
            .RequireAuthorization()
            .WithTags("Objects");

        group.MapGet("", List)
            .WithName("ListObjectDefinitions")
            .WithSummary("List business object definitions for the current workspace")
            .Produces<PagedResult<ObjectDefinitionListItemDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400);

        group.MapPost("", CreateDraft)
            .WithName("CreateObjectDefinitionDraft")
            .WithSummary("Create a business object definition draft")
            .Produces<ObjectDefinitionDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(409);

        group.MapGet("/{id:guid}", Get)
            .WithName("GetObjectDefinition")
            .WithSummary("Get a business object definition")
            .Produces<ObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{id:guid}/draft", SaveDraft)
            .WithName("SaveObjectDefinitionDraft")
            .WithSummary("Save a business object definition draft")
            .Produces<ObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{id:guid}/publish", Publish)
            .WithName("PublishObjectDefinition")
            .WithSummary("Publish a business object definition draft")
            .Produces<ObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        return app;
    }

    private static async Task<IResult> List(
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        Result<PagedResult<ObjectDefinitionListItemDto>> result = await mediator.Send(
            new ListObjectDefinitionsQuery(page, pageSize),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateDraft(
        [FromBody] CreateObjectDefinitionDraftRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ObjectDefinitionDetailDto> result = await mediator.Send(
            new CreateObjectDefinitionDraftCommand(request.Name),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Created($"/api/object-definitions/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> Get(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ObjectDefinitionDetailDto> result = await mediator.Send(
            new GetObjectDefinitionQuery(id),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> SaveDraft(
        Guid id,
        [FromBody] SaveObjectDefinitionDraftRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ObjectDefinitionDetailDto> result = await mediator.Send(
            new SaveObjectDefinitionDraftCommand(
                id,
                request.ExpectedDraftVersion,
                request.Name,
                request.Fields),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> Publish(
        Guid id,
        [FromBody] PublishObjectDefinitionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ObjectDefinitionDetailDto> result = await mediator.Send(
            new PublishObjectDefinitionCommand(id, request.ExpectedDraftVersion),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }
}
