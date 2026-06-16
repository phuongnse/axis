namespace Axis.WorkflowEngine.Contracts;

/// <summary>Cancels all non-terminal workflow executions for a tenant.</summary>
public sealed record CancelTenantExecutionsCommand(Guid tenantId);
