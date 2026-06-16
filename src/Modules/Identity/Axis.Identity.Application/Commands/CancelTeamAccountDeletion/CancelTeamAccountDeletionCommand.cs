using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CancelTeamAccountDeletion;

public sealed record CancelTeamAccountDeletionCommand(Guid TeamAccountId) : ICommand;
