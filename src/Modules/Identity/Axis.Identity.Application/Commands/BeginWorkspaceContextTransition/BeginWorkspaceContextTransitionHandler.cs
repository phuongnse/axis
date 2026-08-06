using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;

public sealed class BeginWorkspaceContextTransitionHandler(
    IWorkspaceRepository workspaces,
    IWorkspaceMembershipRepository memberships,
    IWorkspaceContextTransitionRepository transitions,
    IIdentityAuditOutbox audit,
    IUnitOfWork uow)
    : ICommandHandler<BeginWorkspaceContextTransitionCommand, WorkspaceContextTransitionDto>
{
    public async Task<Result<WorkspaceContextTransitionDto>> Handle(
        BeginWorkspaceContextTransitionCommand command,
        CancellationToken ct)
    {
        if (!await HasActiveAccessAsync(command.SourceWorkspaceId, command.UserId, ct)
            || !await HasActiveAccessAsync(command.TargetWorkspaceId, command.UserId, ct))
        {
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.NotFound,
                "Workspace context is unavailable.");
        }

        DateTime now = DateTime.UtcNow;
        WorkspaceContextTransition transition;
        try
        {
            transition = WorkspaceContextTransition.Begin(
                command.UserId,
                command.SourceWorkspaceId,
                command.TargetWorkspaceId,
                command.SourceCorrelation,
                command.TargetCorrelation,
                now,
                command.ExpiresAt,
                command.RetainUntil);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<WorkspaceContextTransitionDto>(ErrorCodes.InvalidInput, ex.Message);
        }

        await transitions.AddAsync(transition, ct);
        await audit.EnqueueAsync(
            new AuditEventV1(
                Guid.NewGuid(), AuditActorKindV1.Human, command.UserId, command.UserId,
                command.TargetWorkspaceId,
                "workspace.context.transition", "WorkspaceContextTransition", transition.Id,
                "requested", DateTimeOffset.UtcNow, command.CorrelationId.Trim(),
                new Dictionary<string, string> { ["transitionId"] = transition.Id.ToString() }),
            ct);
        await uow.SaveChangesAsync(ct);

        return Result.Success(new WorkspaceContextTransitionDto(
            transition.Id, transition.TargetWorkspaceId, transition.Status.ToString(),
            transition.Revision, transition.ExpiresAt));
    }

    private async Task<bool> HasActiveAccessAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        Workspace? workspace = await workspaces.GetByIdAsync(workspaceId, ct);
        WorkspaceMembership? membership = await memberships.GetActiveAsync(workspaceId, userId, ct);
        return workspace?.Status == WorkspaceStatus.Active
            && membership?.Status == MembershipStatus.Active;
    }
}
