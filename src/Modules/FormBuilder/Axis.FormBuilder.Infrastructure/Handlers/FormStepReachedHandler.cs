using axis.workflowengine.events;
using Axis.FormBuilder.Application.Messages;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.WorkflowEngine.Contracts;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.FormBuilder.Infrastructure.Handlers;

/// <summary>
/// Creates a FormSubmission task when the workflow engine reaches a Form step (US-086).
/// Schedules expiry when a timeout is configured (US-089).
///
/// <para>
/// Cross-module consumer: subscribes to the Avro <see cref="FormStepReachedEvent"/>
/// published by WorkflowEngine over Kafka (ADR-019). Previously consumed the
/// in-process domain event directly — that pattern violated ADR-010 and was
/// tracked as a workaround until this PR.
/// </para>
/// </summary>
internal sealed class FormStepReachedHandler(
    IFormSubmissionRepository submissionRepo,
    IFormRepository formRepo,
    IUnitOfWork uow,
    IMessageBus messageBus,
    ILogger<FormStepReachedHandler> logger)
{
    private const string SystemCreatedBy = "workflow-engine";

    public async Task Handle(FormStepReachedEvent @event, CancellationToken ct)
    {
        Guid executionId = @event.ExecutionId();
        Guid executionStepId = @event.ExecutionStepId();
        Guid organizationId = @event.OrganizationId();
        Guid formDefinitionId = @event.FormDefinitionId();

        if (await submissionRepo.ExistsForExecutionStepAsync(executionId, executionStepId, ct))
        {
            logger.LogInformation(
                "FormStepReachedHandler: submission already exists for execution {ExecutionId} step {StepId}",
                executionId,
                executionStepId);
            return;
        }

        FormDefinition? form = await formRepo.GetByIdAsync(formDefinitionId, organizationId, ct);
        if (form is null)
        {
            logger.LogError(
                "FormStepReachedHandler: form {FormId} not found for org {OrgId}",
                formDefinitionId,
                organizationId);
            return;
        }

        Guid? assigneeUserId = TryParseAssigneeUserId(@event.assigneeExpression);
        DateTimeOffset? expiresAt = @event.timeoutHours.HasValue
            ? DateTimeOffset.UtcNow.AddHours(@event.timeoutHours.Value)
            : null;

        FormSubmission submission = FormSubmission.Create(
            formDefinitionId,
            organizationId,
            executionId,
            executionStepId,
            assigneeUserId,
            assigneeRoleId: null,
            expiresAt,
            createdBy: SystemCreatedBy);

        await submissionRepo.AddAsync(submission, ct);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintException)
        {
            logger.LogWarning(
                "FormStepReachedHandler: concurrent insert for execution {ExecutionId} step {StepId} — skipping",
                executionId,
                executionStepId);
            return;
        }

        if (expiresAt.HasValue)
        {
            TimeSpan delay = expiresAt.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await messageBus.ScheduleAsync(
                    new ExpireFormSubmissionMessage(submission.Id, submission.OrganizationId),
                    expiresAt.Value);
            }
        }

        logger.LogInformation(
            "FormStepReachedHandler: created form task {SubmissionId} for execution {ExecutionId}",
            submission.Id,
            executionId);
    }

    private static Guid? TryParseAssigneeUserId(string? assigneeExpression)
    {
        if (string.IsNullOrWhiteSpace(assigneeExpression))
            return null;

        string trimmed = assigneeExpression.Trim();
        if (Guid.TryParse(trimmed, out Guid direct))
            return direct;

        if (trimmed.StartsWith("{{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal))
        {
            string inner = trimmed[2..^2].Trim();
            if (Guid.TryParse(inner, out Guid wrapped))
                return wrapped;
        }

        return null;
    }
}
