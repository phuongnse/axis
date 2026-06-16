using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.ConfigureStep;

public sealed record ConfigureStepCommand(
    Guid WorkflowId,
    Guid TeamAccountId,
    Guid StepId,
    string Name,
    IReadOnlyDictionary<string, object?>? Config) : ICommand;
