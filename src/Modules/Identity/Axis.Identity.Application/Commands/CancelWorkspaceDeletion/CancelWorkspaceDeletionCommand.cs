using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CancelWorkspaceDeletion;

public sealed record CancelWorkspaceDeletionCommand(Guid workspaceId) : ICommand;
