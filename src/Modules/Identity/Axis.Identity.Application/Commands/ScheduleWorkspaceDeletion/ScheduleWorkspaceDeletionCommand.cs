using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ScheduleWorkspaceDeletion;

public sealed record ScheduleWorkspaceDeletionCommand(
    Guid workspaceId,
    Guid RequestedByUserId,
    string ConfirmationName) : ICommand;
