using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.ConfigureStep;

public sealed record ConfigureStepCommand(
    Guid WorkflowId,
    Guid workspaceId,
    Guid StepId,
    string Name,
    IReadOnlyDictionary<string, object?>? Config) : ICommand;
