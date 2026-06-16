using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Commands.AddStep;

public sealed record AddStepCommand(
    Guid WorkflowId,
    Guid TeamAccountId,
    string Name,
    StepType StepType,
    IReadOnlyDictionary<string, object?>? Config) : ICommand<Guid>;
