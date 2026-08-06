using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;

public sealed class CompleteWorkspaceContextTransitionHandler(
    IWorkspaceContextTransitionRepository transitions,
    IWorkspaceMembershipRepository memberships,
    IIdentityAuditOutbox audit,
    IUnitOfWork uow,
    TimeProvider clock)
    : ICommandHandler<CompleteWorkspaceContextTransitionCommand, WorkspaceContextTransitionDto>
{
    public async Task<Result<WorkspaceContextTransitionDto>> Handle(
        CompleteWorkspaceContextTransitionCommand command,
        CancellationToken ct)
    {
        WorkspaceContextTransition? transition = await transitions.GetByIdAsync(command.TransitionId, ct);
        if (!MatchesTarget(transition, command))
            return Unavailable();

        if (transition!.Status != WorkspaceContextTransitionStatus.Pending)
            return await ReadTerminalAsync(transition.Id, ct);

        if (!await memberships.HasActiveWorkspaceAccessAsync(
                transition.TargetWorkspaceId,
                command.UserId,
                ct))
        {
            return Unavailable();
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        try
        {
            transition.Complete(transition.Revision, now);
            await audit.EnqueueAsync(
                WorkspaceContextTransitionAudit.CreateTerminal(
                    transition,
                    "completed",
                    command.CorrelationId,
                    now),
                ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<WorkspaceContextTransitionDto>(ErrorCodes.Conflict, ex.Message);
        }

        return await ReadTerminalAsync(transition.Id, ct);
    }

    private async Task<Result<WorkspaceContextTransitionDto>> ReadTerminalAsync(
        Guid transitionId,
        CancellationToken ct)
    {
        WorkspaceContextTransition? persisted = await transitions.GetByIdAsync(transitionId, ct);
        if (persisted is null || persisted.Status == WorkspaceContextTransitionStatus.Pending)
        {
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.Conflict,
                "Workspace context transition is still pending.");
        }

        IdentityAuditOutboxEntry? persistedAudit = await audit.GetAsync(
            persisted.TerminalAuditEventId,
            ct);
        return WorkspaceContextTransitionAudit.IsExpectedTerminal(persistedAudit, persisted)
            ? Result.Success(WorkspaceContextTransitionAudit.ToDto(persisted))
            : Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.BusinessRule,
                "Workspace context transition terminal state could not be confirmed.");
    }

    private static bool MatchesTarget(
        WorkspaceContextTransition? transition,
        CompleteWorkspaceContextTransitionCommand command) =>
        transition is not null
        && transition.UserId == command.UserId
        && StringComparer.Ordinal.Equals(
            transition.TargetCorrelationDigest,
            command.TargetCorrelationDigest)
        && !string.IsNullOrWhiteSpace(command.CorrelationId);

    private static Result<WorkspaceContextTransitionDto> Unavailable() =>
        Result.Failure<WorkspaceContextTransitionDto>(
            ErrorCodes.NotFound,
            "Transition is unavailable.");
}
