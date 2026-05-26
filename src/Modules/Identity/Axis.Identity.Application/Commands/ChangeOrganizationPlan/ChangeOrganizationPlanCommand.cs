using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.ChangeOrganizationPlan;

public sealed record ChangeOrganizationPlanCommand(
    Guid OrganizationId,
    Guid NewPlanId,
    Guid ChangedByUserId) : ICommand;
