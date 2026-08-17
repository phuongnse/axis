using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.InviteWorkspaceMember;
using Axis.Identity.Application.Commands.ResendWorkspaceInvitation;
using Axis.Identity.Application.Commands.RevokeWorkspaceInvitation;
using Axis.Identity.Application.Queries.ListWorkspaceInvitations;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class WorkspaceInvitationEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/workspace-invitations")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithTags("Identity");

        group.MapPost("", Invite)
            .WithName("InviteWorkspaceMember")
            .WithSummary("Invite a member to the active organization workspace")
            .Produces<InviteWorkspaceMemberDto>(StatusCodes.Status200OK)
            .Produces<InviteWorkspaceMemberDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem();

        group.MapGet("", List)
            .WithName("ListWorkspaceInvitations")
            .WithSummary("List invitation lifecycle outcomes for the active workspace")
            .Produces<WorkspaceInvitationPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{invitationId:guid}/resend", Resend)
            .WithName("ResendWorkspaceInvitation")
            .WithSummary("Resend a pending workspace invitation")
            .Produces<WorkspaceInvitationLifecycleDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/{invitationId:guid}/revoke", Revoke)
            .WithName("RevokeWorkspaceInvitation")
            .WithSummary("Revoke a pending workspace invitation")
            .Produces<WorkspaceInvitationLifecycleDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> Invite(
        [FromBody] InviteWorkspaceMemberRequest request,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<InviteWorkspaceMemberDto> result = await mediator.Send(
            new InviteWorkspaceMemberCommand(
                currentUser.UserId,
                currentUser.WorkspaceId,
                request.Email,
                request.RequestedRole,
                CorrelationId(context)),
            ct);
        if (result.IsFailure)
            return result.ToProblemDetails();

        return result.Value.Outcome == "Created"
            ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
            : Results.Ok(result.Value);
    }

    private static async Task<IResult> List(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] WorkspaceInvitationSortField? sortBy,
        [FromQuery] CollectionSortDirection? sortDirection,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        int effectivePage = page == 0 ? 1 : page;
        int effectivePageSize = pageSize == 0 ? 20 : pageSize;
        Result<WorkspaceInvitationPageDto> result = await mediator.Send(
            new ListWorkspaceInvitationsQuery(
                currentUser.UserId,
                currentUser.WorkspaceId,
                effectivePage,
                effectivePageSize,
                sortBy,
                sortDirection),
            ct);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Resend(
        Guid invitationId,
        [FromBody] ChangeWorkspaceInvitationRequest request,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<WorkspaceInvitationLifecycleDto> result = await mediator.Send(
            new ResendWorkspaceInvitationCommand(
                currentUser.UserId,
                currentUser.WorkspaceId,
                invitationId,
                request.ExpectedRevision,
                CorrelationId(context),
                currentUser.DisplayName),
            ct);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Revoke(
        Guid invitationId,
        [FromBody] ChangeWorkspaceInvitationRequest request,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<WorkspaceInvitationLifecycleDto> result = await mediator.Send(
            new RevokeWorkspaceInvitationCommand(
                currentUser.UserId,
                currentUser.WorkspaceId,
                invitationId,
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
