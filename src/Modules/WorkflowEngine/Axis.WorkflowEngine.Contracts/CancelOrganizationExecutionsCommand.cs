namespace Axis.WorkflowEngine.Contracts;

/// <summary>Cancels all non-terminal workflow executions for an organization (US-007).</summary>
public sealed record CancelOrganizationExecutionsCommand(Guid OrganizationId);
