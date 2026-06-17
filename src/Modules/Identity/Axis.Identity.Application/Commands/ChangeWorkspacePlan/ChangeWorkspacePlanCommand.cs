using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ChangeWorkspacePlan;

public sealed record ChangeWorkspacePlanCommand(
    Guid workspaceId,
    Guid NewPlanId,
    Guid ChangedByUserId) : ICommand;
