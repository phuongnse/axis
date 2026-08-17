using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Commands.SaveUnpublishedBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Queries.ListBusinessObjectDefinitions;
using Axis.Identity.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class BusinessObjectDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapBusinessObjectDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/business-object-definitions")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithTags("Business Objects");

        group.MapGet("", List)
            .WithName("ListBusinessObjectDefinitions")
            .WithSummary("List business object definitions for the current workspace")
            .Produces<PagedResult<BusinessObjectDefinitionListItemDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/actions", Actions)
            .WithName("GetBusinessObjectDefinitionCollectionActions")
            .WithSummary("Get authorized business object definition collection actions")
            .Produces<BusinessObjectDefinitionCollectionActionsDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("", CreateUnpublished)
            .WithName("CreateBusinessObjectDefinition")
            .WithSummary("Create an unpublished business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}", Get)
            .WithName("GetBusinessObjectDefinition")
            .WithSummary("Get a business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{id:guid}/unpublished", SaveUnpublished)
            .WithName("SaveUnpublishedBusinessObjectDefinition")
            .WithSummary("Save an unpublished business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{id:guid}/publish", Publish)
            .WithName("PublishBusinessObjectDefinition")
            .WithSummary("Publish an unpublished business object definition")
            .Produces<BusinessObjectDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> List(
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? query = null,
        [FromQuery] string? language = null,
        [FromQuery] BusinessObjectDefinitionSortField? sortBy = null,
        [FromQuery] CollectionSortDirection? sortDirection = null)
    {
        Result<PagedResult<BusinessObjectDefinitionListItemDto>> result = await mediator.Send(
            new ListBusinessObjectDefinitionsQuery(
                page,
                pageSize,
                query,
                language,
                sortBy,
                sortDirection),
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

    private static async Task<IResult> Actions(
        ICurrentUser currentUser,
        ICurrentSubject currentSubject,
        IWorkspaceProductBuilderAuthorization authorization,
        CancellationToken ct)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return Result.Failure<BusinessObjectDefinitionCollectionActionsDto>(
                ErrorCodes.Forbidden,
                "Current workspace scope is required.",
                BusinessObjectsProblemCodes.WorkspaceScopeRequired).ToProblemDetails();

        WorkspaceProductBuilderDecision decision = await BusinessObjectAuthorization.AuthorizeBuilderAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            ct);

        if (!decision.IsAllowed)
        {
            return Result.Failure<BusinessObjectDefinitionCollectionActionsDto>(
                decision.IsUnavailable ? ErrorCodes.Unavailable : ErrorCodes.Forbidden,
                decision.IsUnavailable
                    ? "Product Builder authorization is temporarily unavailable."
                    : "Product Builder authority is required.",
                decision.IsUnavailable
                    ? BusinessObjectsProblemCodes.AuthorizationUnavailable
                    : BusinessObjectsProblemCodes.AccessDenied).ToProblemDetails();
        }

        return Results.Ok(new BusinessObjectDefinitionCollectionActionsDto(true));
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
