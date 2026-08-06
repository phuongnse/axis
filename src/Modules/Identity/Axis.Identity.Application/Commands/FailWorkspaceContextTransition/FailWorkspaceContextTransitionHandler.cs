using Axis.Identity.Application.Commands.BeginWorkspaceContextTransition;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.FailWorkspaceContextTransition;

public sealed class FailWorkspaceContextTransitionHandler(
    IWorkspaceContextTransitionRepository transitions,
    IIdentityAuditOutbox audit,
    IUnitOfWork uow,
    TimeProvider clock)
    : ICommandHandler<FailWorkspaceContextTransitionCommand, WorkspaceContextTransitionDto>
{
    public async Task<Result<WorkspaceContextTransitionDto>> Handle(
        FailWorkspaceContextTransitionCommand command,
        CancellationToken ct)
    {
        WorkspaceContextTransition? transition = await transitions.GetByIdAsync(command.TransitionId, ct);
        if (!MatchesSource(transition, command))
            return Unavailable();

        if (transition!.Status != WorkspaceContextTransitionStatus.Pending)
            return await ReadTerminalAsync(transition.Id, ct);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        try
        {
            transition.Fail(transition.Revision, now);
            await audit.EnqueueAsync(
                WorkspaceContextTransitionAudit.CreateTerminal(
                    transition,
                    "failed",
                    command.CorrelationId,
                    now),
                ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (Exception ex) when (ex is ConcurrencyException or UniqueConstraintException)
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

    private static bool MatchesSource(
        WorkspaceContextTransition? transition,
        FailWorkspaceContextTransitionCommand command) =>
        transition is not null
        && transition.UserId == command.UserId
        && StringComparer.Ordinal.Equals(
            transition.SourceCorrelationDigest,
            command.SourceCorrelationDigest)
        && !string.IsNullOrWhiteSpace(command.CorrelationId);

    private static Result<WorkspaceContextTransitionDto> Unavailable() =>
        Result.Failure<WorkspaceContextTransitionDto>(
            ErrorCodes.NotFound,
            "Transition is unavailable.");
}
