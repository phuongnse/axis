using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ScheduleOrganizationDeletion;

public sealed record ScheduleOrganizationDeletionCommand(
    Guid OrganizationId,
    Guid RequestedByUserId,
    string ConfirmationName) : ICommand;
