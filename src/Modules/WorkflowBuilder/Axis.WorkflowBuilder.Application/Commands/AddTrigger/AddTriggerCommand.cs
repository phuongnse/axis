using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Commands.AddTrigger;

public sealed record AddTriggerCommand(
    Guid WorkflowId,
    Guid OrganizationId,
    TriggerType TriggerType,
    IReadOnlyDictionary<string, object?>? Config) : ICommand;
