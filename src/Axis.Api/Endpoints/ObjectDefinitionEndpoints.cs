using Axis.Api.Extensions;
using Axis.Objects.Application;
using Axis.Objects.Application.Commands.CreateObjectDefinition;
using Axis.Objects.Application.Commands.PublishObjectDefinition;
using Axis.Objects.Application.Commands.SaveUnpublishedObjectDefinition;
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

        group.MapPost("", CreateUnpublished)
            .WithName("CreateObjectDefinition")
            .WithSummary("Create an unpublished business object definition")
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

        group.MapPut("/{id:guid}/unpublished", SaveUnpublished)
            .WithName("SaveUnpublishedObjectDefinition")
            .WithSummary("Save an unpublished business object definition")
            .Produces<ObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{id:guid}/publish", Publish)
            .WithName("PublishObjectDefinition")
            .WithSummary("Publish an unpublished business object definition")
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

    private static async Task<IResult> CreateUnpublished(
        [FromBody] CreateObjectDefinitionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ObjectDefinitionDetailDto> result = await mediator.Send(
            new CreateObjectDefinitionCommand(request.Name),
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

    private static async Task<IResult> SaveUnpublished(
        Guid id,
        [FromBody] SaveUnpublishedObjectDefinitionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<ObjectDefinitionDetailDto> result = await mediator.Send(
            new SaveUnpublishedObjectDefinitionCommand(
                id,
                request.ExpectedRevision,
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
            new PublishObjectDefinitionCommand(id, request.ExpectedRevision),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }
}
