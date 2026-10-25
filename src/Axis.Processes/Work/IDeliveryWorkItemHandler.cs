namespace Axis.Processes.Work;

/// <summary>
/// Marks the handler of a work item kind that delivers something. Each target it fails to deliver is
/// counted and, at <c>Worker:DeliveryAttempts</c> failures, becomes an <c>open</c> dead letter (D72).
/// A test finds the delivering kinds by this marker, so a handler of such a kind implements it.
/// </summary>
public interface IDeliveryWorkItemHandler : IExternalWorkItemHandler
{
}
