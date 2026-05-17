using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.Events;

namespace Axis.WorkflowEngine.Domain.Aggregates;

/// <summary>
/// A single runtime instance of a WorkflowDefinition.
/// Tracks execution status, context, and timing.
/// </summary>
public sealed class WorkflowExecution : AggregateRoot<Guid>
{
    private static readonly ExecutionStatus[] TerminalStatuses =
        [ExecutionStatus.Completed, ExecutionStatus.Failed, ExecutionStatus.Cancelled];

    private Dictionary<string, object?> _context;

    public Guid WorkflowDefinitionId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public ExecutionStatus Status { get; private set; }
    public TriggerType TriggerType { get; private set; }
    public Guid? TriggeredByUserId { get; private set; }
    public Guid? RetryOfExecutionId { get; private set; }
    public string? ErrorMessage { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyDictionary<string, object?> Context => _context;

    private WorkflowExecution() : base(default) { _context = new Dictionary<string, object?>(); } // EF Core materialisation

    private WorkflowExecution(
        Guid id,
        Guid workflowDefinitionId,
        Guid organizationId,
        TriggerType triggerType,
        Guid? triggeredByUserId,
        Guid? retryOfExecutionId,
        IReadOnlyDictionary<string, object?> input,
        DateTime createdAt)
        : base(id)
    {
        WorkflowDefinitionId = workflowDefinitionId;
        OrganizationId = organizationId;
        Status = ExecutionStatus.Pending;
        TriggerType = triggerType;
        TriggeredByUserId = triggeredByUserId;
        RetryOfExecutionId = retryOfExecutionId;
        _context = new Dictionary<string, object?>(input);
        CreatedAt = createdAt;
    }

    public static WorkflowExecution Create(
        Guid workflowDefinitionId,
        Guid organizationId,
        TriggerType triggerType,
        Guid? triggeredByUserId,
        IReadOnlyDictionary<string, object?> input)
    {
        DateTime now = DateTime.UtcNow;
        WorkflowExecution exec = new WorkflowExecution(
            Guid.NewGuid(), workflowDefinitionId, organizationId,
            triggerType, triggeredByUserId, null, input, now);

        exec.RaiseDomainEvent(new ExecutionStarted(exec.Id, workflowDefinitionId, organizationId));
        return exec;
    }

    /// <summary>Transitions Pending → Running when the first step begins.</summary>
    public void Start()
    {
        if (Status != ExecutionStatus.Pending)
            throw new InvalidOperationException($"Execution is already in status '{Status}' and cannot be started.");

        Status = ExecutionStatus.Running;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>Transitions Running → Completed when all steps finish successfully.</summary>
    public void Complete()
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException("Only a running execution can be completed.");

        Status = ExecutionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ExecutionCompleted(Id, OrganizationId));
    }

    /// <summary>Transitions Running → Failed with error details.</summary>
    public void Fail(string errorMessage)
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException("Only a running execution can be marked as failed.");

        Status = ExecutionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ExecutionFailed(Id, OrganizationId, errorMessage));
    }

    /// <summary>US-092: Cancels a Pending or Running execution.</summary>
    public void Cancel()
    {
        if (TerminalStatuses.Contains(Status))
            throw new InvalidOperationException(
                $"Cannot cancel an execution with status '{Status}'.");

        Status = ExecutionStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ExecutionCancelled(Id, OrganizationId));
    }

    /// <summary>Merges step output into the running execution context.</summary>
    public void MergeContext(IReadOnlyDictionary<string, object?> stepOutput)
    {
        foreach (KeyValuePair<string, object?> kvp in stepOutput)
            _context[kvp.Key] = kvp.Value;
    }

    /// <summary>US-100: Creates a retry execution linked to this failed execution.</summary>
    public WorkflowExecution CreateRetry(Guid? retriedByUserId)
        => CreateRetryCore(retriedByUserId, _context);

    /// <summary>US-102: Creates a retry execution using a user-supplied modified context.</summary>
    public WorkflowExecution CreateRetryWithModifiedContext(
        Guid? retriedByUserId, IReadOnlyDictionary<string, object?> modifiedContext)
        => CreateRetryCore(retriedByUserId, modifiedContext);

    private WorkflowExecution CreateRetryCore(
        Guid? retriedByUserId, IReadOnlyDictionary<string, object?> context)
    {
        if (Status != ExecutionStatus.Failed)
            throw new InvalidOperationException("Only failed executions can be retried.");

        DateTime now = DateTime.UtcNow;
        WorkflowExecution retry = new WorkflowExecution(
            Guid.NewGuid(), WorkflowDefinitionId, OrganizationId,
            TriggerType, retriedByUserId, Id, context, now);

        retry.RaiseDomainEvent(new ExecutionStarted(retry.Id, WorkflowDefinitionId, OrganizationId));
        return retry;
    }
}
