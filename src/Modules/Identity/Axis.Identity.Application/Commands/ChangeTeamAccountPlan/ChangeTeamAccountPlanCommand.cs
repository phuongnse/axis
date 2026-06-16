using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ChangeTeamAccountPlan;

public sealed record ChangeTeamAccountPlanCommand(
    Guid TeamAccountId,
    Guid NewPlanId,
    Guid ChangedByUserId) : ICommand;
