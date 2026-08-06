using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
namespace Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;

public sealed class CompensateWorkspaceContextTransitionHandler(IWorkspaceContextTransitionRepository transitions, IIdentityAuditOutbox audit, IUnitOfWork uow) : ICommandHandler<CompensateWorkspaceContextTransitionCommand, WorkspaceContextTransitionResultDto>
{ public async Task<Result<WorkspaceContextTransitionResultDto>> Handle(CompensateWorkspaceContextTransitionCommand command, CancellationToken ct) { WorkspaceContextTransition? transition = await transitions.GetByIdAsync(command.TransitionId, ct); if (transition is null || transition.UserId != command.UserId) return Result.Failure<WorkspaceContextTransitionResultDto>(ErrorCodes.NotFound, "Transition is unavailable."); if (transition.Status == WorkspaceContextTransitionStatus.Compensated) return Result.Success(new WorkspaceContextTransitionResultDto(transition.Id, transition.Status.ToString(), transition.Revision)); try { transition.Compensate(command.ExpectedRevision, DateTime.UtcNow); } catch (InvalidOperationException ex) { return Result.Failure<WorkspaceContextTransitionResultDto>(ErrorCodes.Conflict, ex.Message); } await audit.EnqueueAsync(new(Guid.NewGuid(), AuditActorKindV1.Human, command.UserId, command.UserId, transition.TargetWorkspaceId, "workspace.context.transition", "WorkspaceContextTransition", transition.Id, "compensated", DateTimeOffset.UtcNow, command.CorrelationId.Trim(), new Dictionary<string, string> { { "transitionId", transition.Id.ToString() } }), ct); await uow.SaveChangesAsync(ct); return Result.Success(new WorkspaceContextTransitionResultDto(transition.Id, transition.Status.ToString(), transition.Revision)); } }

