namespace Axis.WorkflowEngine.Contracts;

/// <summary>Cancels all non-terminal workflow executions for a team account.</summary>
public sealed record CancelTeamAccountExecutionsCommand(Guid TeamAccountId);
