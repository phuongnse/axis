namespace Axis.WorkflowEngine.Application.Services;

/// <summary>
/// Application-layer abstraction for dispatching internal engine messages between step handlers.
/// Implemented in Infrastructure using Wolverine's IMessageBus so Application has no
/// direct dependency on Wolverine.
/// </summary>
public interface IStepDispatcher
{
    /// <summary>Publishes a message for async processing by a Wolverine handler.</summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : notnull;
}
