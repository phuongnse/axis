namespace Axis.WorkflowEngine.Contracts;

/// <summary>Cancels all non-terminal workflow executions for a workspace.</summary>
public sealed record CancelWorkspaceExecutionsCommand(Guid workspaceId);
