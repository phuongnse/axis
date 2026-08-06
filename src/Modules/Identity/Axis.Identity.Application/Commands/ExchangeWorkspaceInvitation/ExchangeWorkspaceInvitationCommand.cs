using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ExchangeWorkspaceInvitation;

public sealed record ExchangeWorkspaceInvitationCommand(
    string RawToken,
    string RequestPartition,
    string CorrelationId) : ICommand<WorkspaceInvitationExchangeDto>;
