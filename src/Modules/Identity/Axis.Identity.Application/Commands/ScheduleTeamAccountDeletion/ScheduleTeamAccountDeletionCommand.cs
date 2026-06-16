using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ScheduleTeamAccountDeletion;

public sealed record ScheduleTeamAccountDeletionCommand(
    Guid TeamAccountId,
    Guid RequestedByUserId,
    string ConfirmationName) : ICommand;
