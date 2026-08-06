using Axis.Shared.Application.CQRS;
namespace Axis.Identity.Application.Commands.CompleteWorkspaceContextTransition;

public sealed record CompleteWorkspaceContextTransitionCommand(Guid TransitionId, Guid UserId, string TargetCorrelation, int ExpectedRevision, string CorrelationId) : ICommand<WorkspaceContextTransitionResultDto>;
public sealed record WorkspaceContextTransitionResultDto(Guid TransitionId, string Status, int Revision);
