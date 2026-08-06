using Axis.Shared.Application.CQRS;
namespace Axis.Identity.Application.Commands.CompensateWorkspaceContextTransition;

public sealed record CompensateWorkspaceContextTransitionCommand(Guid TransitionId, Guid UserId, int ExpectedRevision, string CorrelationId) : ICommand<WorkspaceContextTransitionResultDto>;
public sealed record WorkspaceContextTransitionResultDto(Guid TransitionId, string Status, int Revision);
