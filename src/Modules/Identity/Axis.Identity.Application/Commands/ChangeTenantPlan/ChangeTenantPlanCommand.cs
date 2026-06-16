using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ChangeTenantPlan;

public sealed record ChangeTenantPlanCommand(
    Guid tenantId,
    Guid NewPlanId,
    Guid ChangedByUserId) : ICommand;
