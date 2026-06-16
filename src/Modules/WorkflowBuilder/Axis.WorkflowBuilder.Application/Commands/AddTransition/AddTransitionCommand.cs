using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.AddTransition;

public sealed record AddTransitionCommand(
    Guid WorkflowId,
    Guid TeamAccountId,
    Guid FromStepId,
    Guid ToStepId,
    string? Label) : ICommand;
