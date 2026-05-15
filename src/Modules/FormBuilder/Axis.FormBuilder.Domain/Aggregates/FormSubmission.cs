using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Aggregates;

public sealed class FormSubmission : AggregateRoot<Guid>
{
    public Guid FormDefinitionId { get; private set; }
    public Guid OrganizationId { get; private set; }
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
        Guid organizationId,
        Guid executionId,
        Guid executionStepId,
        Guid? assigneeUserId,
        Guid? assigneeRoleId,
        DateTimeOffset? expiresAt,
        string createdBy) : base(id)
    {
        FormDefinitionId = formDefinitionId;
        OrganizationId = organizationId;
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
        Guid organizationId,
        Guid executionId,
        Guid executionStepId,
        Guid? assigneeUserId,
        Guid? assigneeRoleId,
        DateTimeOffset? expiresAt,
        string createdBy)
    {
        FormSubmission submission = new(
            Guid.NewGuid(),
            formDefinitionId,
            organizationId,
            executionId,
            executionStepId,
            assigneeUserId,
            assigneeRoleId,
            expiresAt,
            createdBy);

        submission.RaiseDomainEvent(new FormTaskCreated(
            submission.Id,
            formDefinitionId,
            organizationId,
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

        Status = FormSubmissionStatus.Submitted;
        SubmittedByUserId = submittedByUserId;
        SubmittedData = data;
        SubmittedAt = DateTimeOffset.UtcNow;

        RaiseDomainEvent(new FormTaskSubmitted(
            Id,
            FormDefinitionId,
            OrganizationId,
            ExecutionId,
            ExecutionStepId,
            data));
    }

    public void Expire()
    {
        if (Status != FormSubmissionStatus.Pending)
            throw new InvalidOperationException($"Cannot expire a form task that is not in Pending status. Current status: {Status}.");

        Status = FormSubmissionStatus.Expired;

        RaiseDomainEvent(new FormTaskExpired(
            Id,
            FormDefinitionId,
            OrganizationId,
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
            OrganizationId,
            ExecutionId));
    }
}
