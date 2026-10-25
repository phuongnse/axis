namespace Axis.Processes.Instances;

/// <summary>How the worker limits failing deliveries.</summary>
/// <param name="Attempts">
/// The failures of one delivery target that make it a dead letter. At least 1.
/// </param>
public sealed record DeliverySettings(int Attempts);
