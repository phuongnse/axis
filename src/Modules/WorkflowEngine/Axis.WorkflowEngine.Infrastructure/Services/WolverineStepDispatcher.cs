using Axis.WorkflowEngine.Application.Services;
using Wolverine;

namespace Axis.WorkflowEngine.Infrastructure.Services;

/// <summary>
/// Wraps Wolverine's IMessageBus to implement the Application-layer IStepDispatcher interface,
/// keeping Application handlers free of direct Wolverine dependency.
/// </summary>
internal sealed class WolverineStepDispatcher(IMessageBus bus) : IStepDispatcher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : notnull
        => bus.PublishAsync(message).AsTask();
}
