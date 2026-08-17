using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.SetWorkspaceProductBuilder;
using Axis.Identity.Application.Queries.ListWorkspaceProductBuilders;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class WorkspaceProductBuilderEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceProductBuilderEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/workspace-product-builders")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithTags("Identity");

        group.MapGet("", List)
            .WithName("ListWorkspaceProductBuilders")
            .WithSummary("List active human members and Product Builder authority for the active workspace")
            .Produces<IReadOnlyList<WorkspaceProductBuilderDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{userId:guid}/grant", Grant)
            .WithName("GrantWorkspaceProductBuilder")
            .WithSummary("Grant Product Builder authority to another active workspace member")
            .Produces<WorkspaceProductBuilderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{userId:guid}/revoke", Revoke)
            .WithName("RevokeWorkspaceProductBuilder")
            .WithSummary("Revoke Product Builder authority from another active workspace member")
            .Produces<WorkspaceProductBuilderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> List(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<IReadOnlyList<WorkspaceProductBuilderDto>> result = await mediator.Send(
            new ListWorkspaceProductBuildersQuery(currentUser.UserId, currentUser.WorkspaceId),
            ct);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static Task<IResult> Grant(
        Guid userId,
        [FromBody] ChangeWorkspaceProductBuilderRequest request,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct) =>
        Change(userId, request, enabled: true, context, currentUser, mediator, ct);

    private static Task<IResult> Revoke(
        Guid userId,
        [FromBody] ChangeWorkspaceProductBuilderRequest request,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct) =>
        Change(userId, request, enabled: false, context, currentUser, mediator, ct);

    private static async Task<IResult> Change(
        Guid userId,
        ChangeWorkspaceProductBuilderRequest request,
        bool enabled,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<WorkspaceProductBuilderDto> result = await mediator.Send(
            new SetWorkspaceProductBuilderCommand(
                currentUser.UserId,
                currentUser.WorkspaceId,
                userId,
                enabled,
                request.ExpectedRevision,
                CorrelationId(context),
                currentUser.DisplayName),
            ct);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value)
            && value is string correlationId
                ? correlationId
                : context.TraceIdentifier;
}
