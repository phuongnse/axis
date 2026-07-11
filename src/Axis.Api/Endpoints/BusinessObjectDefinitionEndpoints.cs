using Axis.Api.Extensions;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Commands.SaveUnpublishedBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class BusinessObjectDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapBusinessObjectDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/business-object-definitions")
            .RequireAuthorization()
            .WithTags("Business Objects");

        group.MapGet("", List)
            .WithName("ListBusinessObjectDefinitions")
            .WithSummary("List business object definitions for the current workspace")
            .Produces<PagedResult<BusinessObjectDefinitionListItemDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400);

        group.MapPost("", CreateUnpublished)
            .WithName("CreateBusinessObjectDefinition")
            .WithSummary("Create an unpublished business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(409);

        group.MapGet("/{id:guid}", Get)
            .WithName("GetBusinessObjectDefinition")
            .WithSummary("Get a business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{id:guid}/unpublished", SaveUnpublished)
            .WithName("SaveUnpublishedBusinessObjectDefinition")
            .WithSummary("Save an unpublished business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{id:guid}/publish", Publish)
            .WithName("PublishBusinessObjectDefinition")
            .WithSummary("Publish an unpublished business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>()
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
        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await mediator.Send(
            new ListBusinessObjectDefinitionsQuery(page, pageSize),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateUnpublished(
        [FromBody] CreateBusinessObjectDefinitionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<BusinessObjectDefinitionDetailDto> result = await mediator.Send(
            new CreateBusinessObjectDefinitionCommand(request.Name),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Created($"/api/business-object-definitions/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> Get(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        Result<BusinessObjectDefinitionDetailDto> result = await mediator.Send(
            new GetBusinessObjectDefinitionQuery(id),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> SaveUnpublished(
        Guid id,
        [FromBody] SaveUnpublishedBusinessObjectDefinitionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<BusinessObjectDefinitionDetailDto> result = await mediator.Send(
            new SaveUnpublishedBusinessObjectDefinitionCommand(
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
        [FromBody] PublishBusinessObjectDefinitionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result<BusinessObjectDefinitionDetailDto> result = await mediator.Send(
            new PublishBusinessObjectDefinitionCommand(id, request.ExpectedRevision),
            ct);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Ok(result.Value);
    }
}
