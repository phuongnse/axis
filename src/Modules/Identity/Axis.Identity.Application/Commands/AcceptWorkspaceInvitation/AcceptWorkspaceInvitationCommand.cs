using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.AcceptWorkspaceInvitation;

public sealed record AcceptWorkspaceInvitationCommand(
    string HandoffHash,
    Guid UserId,
    string CorrelationId) : ICommand<WorkspaceInvitationAcceptanceDto>;
