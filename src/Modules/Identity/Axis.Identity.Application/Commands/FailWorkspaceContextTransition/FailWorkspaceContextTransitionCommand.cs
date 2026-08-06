using Axis.Shared.Application.CQRS;
namespace Axis.Identity.Application.Commands.FailWorkspaceContextTransition;

public sealed record FailWorkspaceContextTransitionCommand(Guid TransitionId, Guid UserId, int ExpectedRevision, string CorrelationId) : ICommand<WorkspaceContextTransitionResultDto>;
public sealed record WorkspaceContextTransitionResultDto(Guid TransitionId, string Status, int Revision);
