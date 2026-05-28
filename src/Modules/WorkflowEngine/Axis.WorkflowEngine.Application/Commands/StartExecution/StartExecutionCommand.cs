using Axis.Shared.Application.CQRS;
using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Application.Commands.StartExecution;

/// <summary>Create a new execution for an active workflow.</summary>
public sealed record StartExecutionCommand(
    Guid WorkflowDefinitionId,
    Guid OrganizationId,
    TriggerType TriggerType,
    Guid? TriggeredByUserId,
    IReadOnlyDictionary<string, object?>? Input) : ICommand<Guid>;
