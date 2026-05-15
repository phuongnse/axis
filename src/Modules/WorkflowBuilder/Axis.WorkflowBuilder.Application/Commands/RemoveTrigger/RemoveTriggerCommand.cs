using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Commands.RemoveTrigger;

public sealed record RemoveTriggerCommand(
    Guid WorkflowId,
    Guid OrganizationId,
    TriggerType TriggerType) : ICommand;
