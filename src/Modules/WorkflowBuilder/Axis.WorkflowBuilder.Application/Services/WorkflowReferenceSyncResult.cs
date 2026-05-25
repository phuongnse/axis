namespace Axis.WorkflowBuilder.Application.Services;

/// <summary>Outcome of aligning workflow reference read models with the aggregate (same unit of work).</summary>
public readonly record struct WorkflowReferenceSyncResult(bool HasBrokenReferences);
