namespace Axis.Tenancy.Platform;

/// <summary>
/// The failed work of one tenant as three counts: the instances that are <c>failed</c>, the connector and package calls whose
/// outcome is <c>unknown</c> and the dead letters that are <c>open</c>. It holds numbers only.
/// </summary>
public readonly record struct TenantFailedWorkCounts(long Instances, long UnknownCalls, long DeadLetters);
