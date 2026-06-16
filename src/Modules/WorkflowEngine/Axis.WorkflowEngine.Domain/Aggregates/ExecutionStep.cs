using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Domain.Aggregates;

public sealed class ExecutionStep : Entity<Guid>
{
    public Guid ExecutionId { get; private set; }
    public Guid tenantId { get; private set; }
    public Guid StepDefinitionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public StepType StepType { get; private set; }
    public int DisplayOrder { get; private set; }
    public StepExecutionStatus Status { get; private set; }
    public IReadOnlyDictionary<string, object?>? InputSnapshot { get; private set; }
    public IReadOnlyDictionary<string, object?>? OutputSnapshot { get; private set; }
    public string? ErrorDetails { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsTerminal => Status is
        StepExecutionStatus.Completed or
        StepExecutionStatus.Failed or
        StepExecutionStatus.Cancelled;

    private ExecutionStep() : base(Guid.Empty) { }

    private ExecutionStep(
        Guid id,
        Guid executionId,
        Guid tenantId,
        Guid stepDefinitionId,
        string name,
        StepType stepType,
        int displayOrder) : base(id)
    {
        ExecutionId = executionId;
        this.tenantId = tenantId;
        StepDefinitionId = stepDefinitionId;
        Name = name;
        StepType = stepType;
        DisplayOrder = displayOrder;
        Status = StepExecutionStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static ExecutionStep Create(
        Guid executionId,
        Guid tenantId,
        Guid stepDefinitionId,
        string name,
        StepType stepType,
        int displayOrder)
    {
        if (executionId == Guid.Empty) throw new ArgumentException("ExecutionId must not be empty.", nameof(executionId));
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId must not be empty.", nameof(tenantId));
        if (stepDefinitionId == Guid.Empty) throw new ArgumentException("StepDefinitionId must not be empty.", nameof(stepDefinitionId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name must not be blank.", nameof(name));
        if (displayOrder < 0) throw new ArgumentOutOfRangeException(nameof(displayOrder), "DisplayOrder must be non-negative.");

        return new ExecutionStep(
            Guid.NewGuid(),
            executionId,
            tenantId,
            stepDefinitionId,
            name,
            stepType,
            displayOrder);
    }

    public void Start(IReadOnlyDictionary<string, object?> inputSnapshot)
    {
        if (Status != StepExecutionStatus.Pending)
            throw new InvalidOperationException($"Cannot start a step that is not in Pending status. Current status: {Status}.");

        Status = StepExecutionStatus.Running;
        InputSnapshot = inputSnapshot;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(IReadOnlyDictionary<string, object?> output)
    {
        if (Status is not (StepExecutionStatus.Running or StepExecutionStatus.Waiting))
            throw new InvalidOperationException($"Cannot complete a step that is not Running or Waiting. Current status: {Status}.");

        Status = StepExecutionStatus.Completed;
        OutputSnapshot = output;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string errorDetails)
    {
        if (Status is not (StepExecutionStatus.Running or StepExecutionStatus.Waiting))
            throw new InvalidOperationException(
                $"Cannot fail a step that is not Running or Waiting. Current status: {Status}.");

        Status = StepExecutionStatus.Failed;
        ErrorDetails = errorDetails;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Wait()
    {
        if (Status != StepExecutionStatus.Running)
            throw new InvalidOperationException($"Cannot suspend a step that is not Running. Current status: {Status}.");

        Status = StepExecutionStatus.Waiting;
    }

    public void Skip(string reason)
    {
        if (Status != StepExecutionStatus.Pending)
            throw new InvalidOperationException($"Cannot skip a step that is not in Pending status. Current status: {Status}.");

        Status = StepExecutionStatus.Skipped;
        ErrorDetails = reason;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (IsTerminal || Status == StepExecutionStatus.Skipped)
            throw new InvalidOperationException($"Cannot cancel a step that is already in a terminal state. Current status: {Status}.");

        Status = StepExecutionStatus.Cancelled;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
