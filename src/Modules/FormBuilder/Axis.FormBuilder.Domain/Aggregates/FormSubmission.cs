using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Aggregates;

public sealed class FormSubmission : AggregateRoot<Guid>
{
    public Guid FormDefinitionId { get; private set; }
    public Guid workspaceId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public Guid ExecutionStepId { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public Guid? AssigneeRoleId { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }
    public Guid AccessToken { get; private set; }
    public FormSubmissionStatus Status { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public IReadOnlyDictionary<string, object?> SubmittedData { get; private set; } =
        new Dictionary<string, object?>();
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private FormSubmission() : base(Guid.Empty) { }

    private FormSubmission(
        Guid id,
        Guid formDefinitionId,
        Guid workspaceId,
        Guid executionId,
        Guid executionStepId,
        Guid? assigneeUserId,
        Guid? assigneeRoleId,
        DateTimeOffset? expiresAt,
        string createdBy) : base(id)
    {
        FormDefinitionId = formDefinitionId;
        this.workspaceId = workspaceId;
        ExecutionId = executionId;
        ExecutionStepId = executionStepId;
        AssigneeUserId = assigneeUserId;
        AssigneeRoleId = assigneeRoleId;
        ExpiresAt = expiresAt;
        CreatedBy = createdBy;
        AccessToken = Guid.NewGuid();
        Status = FormSubmissionStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static FormSubmission Create(
        Guid formDefinitionId,
        Guid workspaceId,
        Guid executionId,
        Guid executionStepId,
        Guid? assigneeUserId,
        Guid? assigneeRoleId,
        DateTimeOffset? expiresAt,
        string createdBy)
    {
        if (formDefinitionId == Guid.Empty) throw new ArgumentException("FormDefinitionId must not be empty.", nameof(formDefinitionId));
        if (workspaceId == Guid.Empty) throw new ArgumentException("workspaceId must not be empty.", nameof(workspaceId));
        if (executionId == Guid.Empty) throw new ArgumentException("ExecutionId must not be empty.", nameof(executionId));
        if (executionStepId == Guid.Empty) throw new ArgumentException("ExecutionStepId must not be empty.", nameof(executionStepId));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy must not be blank.", nameof(createdBy));

        FormSubmission submission = new(
            Guid.NewGuid(),
            formDefinitionId,
            workspaceId,
            executionId,
            executionStepId,
            assigneeUserId,
            assigneeRoleId,
            expiresAt,
            createdBy);

        submission.RaiseDomainEvent(new FormTaskCreated(
            submission.Id,
            formDefinitionId,
            workspaceId,
            executionId,
            assigneeUserId,
            assigneeRoleId,
            expiresAt,
            submission.AccessToken));

        return submission;
    }

    public void Submit(Guid submittedByUserId, IReadOnlyDictionary<string, object?> data)
    {
        if (Status != FormSubmissionStatus.Pending)
            throw new InvalidOperationException($"Cannot submit a form task that is already {Status}.");

        Dictionary<string, object?> snapshot = new(data);

        Status = FormSubmissionStatus.Submitted;
        SubmittedByUserId = submittedByUserId;
        SubmittedData = snapshot;
        SubmittedAt = DateTimeOffset.UtcNow;

        RaiseDomainEvent(new FormTaskSubmitted(
            Id,
            FormDefinitionId,
            workspaceId,
            ExecutionId,
            ExecutionStepId,
            snapshot));
    }

    public void Expire()
    {
        if (Status != FormSubmissionStatus.Pending)
            throw new InvalidOperationException($"Cannot expire a form task that is not in Pending status. Current status: {Status}.");

        Status = FormSubmissionStatus.Expired;

        RaiseDomainEvent(new FormTaskExpired(
            Id,
            FormDefinitionId,
            workspaceId,
            ExecutionId,
            ExecutionStepId));
    }

    public void Cancel()
    {
        if (Status != FormSubmissionStatus.Pending)
            throw new InvalidOperationException($"Cannot cancel a form task that is not in Pending status. Current status: {Status}.");

        Status = FormSubmissionStatus.Cancelled;

        RaiseDomainEvent(new FormTaskCancelled(
            Id,
            FormDefinitionId,
            workspaceId,
            ExecutionId));
    }
}
