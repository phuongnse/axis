namespace Axis.Processes.Work;

/// <summary>How the worker limits the work items it runs.</summary>
/// <param name="StatementTimeout">
/// The <c>statement_timeout</c> of every statement a work item runs. At least one millisecond.
/// </param>
public sealed record WorkItemSettings(TimeSpan StatementTimeout)
{
    /// <summary>The limit when <c>Worker:StatementTimeout</c> is not set.</summary>
    public static readonly TimeSpan DefaultStatementTimeout = TimeSpan.FromMinutes(1);
}
