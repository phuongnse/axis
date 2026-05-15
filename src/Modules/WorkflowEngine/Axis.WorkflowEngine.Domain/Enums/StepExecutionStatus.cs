namespace Axis.WorkflowEngine.Domain.Enums;

public enum StepExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Waiting,
    Cancelled
}
