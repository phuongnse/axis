using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ExpireWorkspaceContextTransition;

public sealed class ExpireWorkspaceContextTransitionHandler(
    IWorkspaceContextTransitionRepository transitions,
    IIdentityAuditOutbox audit,
    IUnitOfWork uow,
    TimeProvider clock)
    : ICommandHandler<ExpireWorkspaceContextTransitionCommand, WorkspaceContextTransitionDto>
{
    public async Task<Result<WorkspaceContextTransitionDto>> Handle(
        ExpireWorkspaceContextTransitionCommand command,
        CancellationToken ct)
    {
        WorkspaceContextTransition? transition = await transitions.GetByIdAsync(command.TransitionId, ct);
        if (transition is null
            || transition.UserId != command.UserId
            || !StringComparer.Ordinal.Equals(
                transition.SourceCorrelationDigest,
                command.SourceCorrelationDigest))
        {
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.NotFound,
                "Transition is unavailable.");
        }

        if (transition.Status != WorkspaceContextTransitionStatus.Pending)
            return await ReadTerminalAsync(transition.Id, ct);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (now <= transition.ExpiresAt)
        {
            return Result.Failure<WorkspaceContextTransitionDto>(
                ErrorCodes.Conflict,
                "Workspace context transition has not expired.");
        }

        try
        {
            transition.Compensate(transition.Revision, now);
            await audit.EnqueueAsync(
                WorkspaceContextTransitionAudit.CreateTerminal(
                    transition,
                    "compensated",
                    $"workspace-transition-expiry:{transition.Id:N}",
                    now,
                    AuditActorKindV1.System),
                ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
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
}
