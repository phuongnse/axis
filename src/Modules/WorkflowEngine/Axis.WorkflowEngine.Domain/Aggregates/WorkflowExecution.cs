using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.Events;
using Axis.WorkflowEngine.Domain.ReadModels;

namespace Axis.WorkflowEngine.Domain.Aggregates;

public sealed class WorkflowExecution : AggregateRoot<Guid>
{
    private static readonly ExecutionStatus[] TerminalStatuses =
        [ExecutionStatus.Completed, ExecutionStatus.Failed, ExecutionStatus.Cancelled];

    private Dictionary<string, object?> _context;
    private List<ExecutionStep> _steps = [];

    public Guid WorkflowDefinitionId { get; private set; }
    public Guid workspaceId { get; private set; }
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
    public IReadOnlyList<ExecutionStep> Steps => _steps.AsReadOnly();

    private WorkflowExecution() : base(default) { _context = new Dictionary<string, object?>(); }

    private WorkflowExecution(
        Guid id,
        Guid workflowDefinitionId,
        Guid workspaceId,
        TriggerType triggerType,
        Guid? triggeredByUserId,
        Guid? retryOfExecutionId,
        IReadOnlyDictionary<string, object?> input,
        DateTime createdAt)
        : base(id)
    {
        WorkflowDefinitionId = workflowDefinitionId;
        this.workspaceId = workspaceId;
        Status = ExecutionStatus.Pending;
        TriggerType = triggerType;
        TriggeredByUserId = triggeredByUserId;
        RetryOfExecutionId = retryOfExecutionId;
        _context = new Dictionary<string, object?>(input);
        CreatedAt = createdAt;
    }

    public static WorkflowExecution Create(
        Guid workflowDefinitionId,
        Guid workspaceId,
        TriggerType triggerType,
        Guid? triggeredByUserId,
        IReadOnlyDictionary<string, object?> input)
    {
        DateTime now = DateTime.UtcNow;
        WorkflowExecution exec = new WorkflowExecution(
            Guid.NewGuid(), workflowDefinitionId, workspaceId,
            triggerType, triggeredByUserId, null, input, now);

        exec.RaiseDomainEvent(new ExecutionStarted(exec.Id, workflowDefinitionId, workspaceId));
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
        RaiseDomainEvent(new ExecutionCompleted(Id, workspaceId));
    }

    /// <summary>Transitions Running → Failed with error details.</summary>
    public void Fail(string errorMessage)
    {
        if (Status != ExecutionStatus.Running)
            throw new InvalidOperationException("Only a running execution can be marked as failed.");

        Status = ExecutionStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ExecutionFailed(Id, workspaceId, errorMessage));
    }

    /// <summary>Cancels a Pending or Running execution.</summary>
    public void Cancel()
    {
        if (TerminalStatuses.Contains(Status))
            throw new InvalidOperationException(
                $"Cannot cancel an execution with status '{Status}'.");

        Status = ExecutionStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        RaiseDomainEvent(new ExecutionCancelled(Id, workspaceId));
    }

    /// <summary>Merges step output into the running execution context.</summary>
    public void MergeContext(IReadOnlyDictionary<string, object?> stepOutput)
    {
        foreach (KeyValuePair<string, object?> kvp in stepOutput)
            _context[kvp.Key] = kvp.Value;
    }

    public ExecutionStep AddStep(
        Guid stepDefinitionId, string name, StepType stepType, int displayOrder)
    {
        ExecutionStep step = ExecutionStep.Create(Id, workspaceId, stepDefinitionId, name, stepType, displayOrder);
        _steps.Add(step);
        return step;
    }

    /// <summary>
    /// Creates ExecutionStep records for every step in the workflow snapshot (upfront creation).
    /// Steps from the WorkflowBuilder StepType string are mapped to WorkflowEngine StepType.
    /// Called by StartExecutionHandler before dispatching the first step message.
    /// </summary>
    public void InitialiseSteps(IReadOnlyList<StepDefinitionSnapshot> snapshotSteps)
    {
        if (_steps.Count > 0)
            throw new InvalidOperationException("Steps are already initialised for this execution.");

        foreach (StepDefinitionSnapshot stepDef in snapshotSteps.OrderBy(s => s.DisplayOrder))
            _steps.Add(ExecutionStep.Create(Id, workspaceId, stepDef.Id, stepDef.Name, stepDef.StepType, stepDef.DisplayOrder));
    }

    public void StartStep(Guid stepId, IReadOnlyDictionary<string, object?> inputSnapshot)
        => GetStep(stepId).Start(inputSnapshot);

    public void CompleteStep(Guid stepId, IReadOnlyDictionary<string, object?> output)
    {
        GetStep(stepId).Complete(output);
        RaiseDomainEvent(new ExecutionStepCompleted(Id, stepId, workspaceId, output));
    }

    public void FailStep(Guid stepId, string errorDetails)
    {
        GetStep(stepId).Fail(errorDetails);
        RaiseDomainEvent(new ExecutionStepFailed(Id, stepId, workspaceId, errorDetails));
    }

    /// <summary>Transitions a Form step to Waiting and raises the FormStepReached event for cross-module dispatch.</summary>
    public void ReachFormStep(
        Guid stepId,
        Guid formDefinitionId,
        string? assigneeExpression,
        int? timeoutHours)
    {
        GetStep(stepId).Wait();
        RaiseDomainEvent(new FormStepReached(
            Id, stepId, WorkflowDefinitionId, workspaceId,
            formDefinitionId, assigneeExpression, timeoutHours));
    }

    public void WaitStep(Guid stepId)
        => GetStep(stepId).Wait();

    public void SkipStep(Guid stepId, string reason)
        => GetStep(stepId).Skip(reason);

    public void CancelStep(Guid stepId)
        => GetStep(stepId).Cancel();

    private ExecutionStep GetStep(Guid stepId)
    {
        ExecutionStep? step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
            throw new InvalidOperationException($"Step '{stepId}' not found in execution '{Id}'.");
        return step;
    }

    /// <summary>Creates a retry execution linked to this failed execution.</summary>
    public WorkflowExecution CreateRetry(Guid? retriedByUserId)
        => CreateRetryCore(retriedByUserId, _context);

    /// <summary>Creates a retry execution using a user-supplied modified context.</summary>
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
            Guid.NewGuid(), WorkflowDefinitionId, workspaceId,
            TriggerType, retriedByUserId, Id, context, now);

        retry.RaiseDomainEvent(new ExecutionStarted(retry.Id, WorkflowDefinitionId, workspaceId));
        return retry;
    }
}
