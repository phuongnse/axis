using Axis.Shared.Application.CQRS;

namespace Axis.WorkflowBuilder.Application.Commands.AddTransition;

public sealed record AddTransitionCommand(
    Guid WorkflowId,
    Guid tenantId,
    Guid FromStepId,
    Guid ToStepId,
    string? Label) : ICommand;
