using Axis.FormBuilder.Application.Messages;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Events;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.FormBuilder.Infrastructure.Handlers;

/// <summary>
/// Creates a FormSubmission task when the workflow engine reaches a Form step (US-086).
/// Schedules expiry when a timeout is configured (US-089).
/// </summary>
internal sealed class FormStepReachedHandler(
    IFormSubmissionRepository submissionRepo,
    IFormRepository formRepo,
    IUnitOfWork uow,
    IMessageBus messageBus,
    ILogger<FormStepReachedHandler> logger)
{
    public async Task Handle(FormStepReached @event, CancellationToken ct)
    {
        if (await submissionRepo.ExistsForExecutionStepAsync(@event.ExecutionId, @event.ExecutionStepId, ct))
        {
            logger.LogInformation(
                "FormStepReachedHandler: submission already exists for execution {ExecutionId} step {StepId}",
                @event.ExecutionId,
                @event.ExecutionStepId);
            return;
        }

        FormDefinition? form = await formRepo.GetByIdAsync(@event.FormDefinitionId, @event.OrganizationId, ct);
        if (form is null)
        {
            logger.LogError(
                "FormStepReachedHandler: form {FormId} not found for org {OrgId}",
                @event.FormDefinitionId,
                @event.OrganizationId);
            return;
        }

        Guid? assigneeUserId = TryParseAssigneeUserId(@event.AssigneeExpression);
        DateTimeOffset? expiresAt = @event.TimeoutHours.HasValue
            ? DateTimeOffset.UtcNow.AddHours(@event.TimeoutHours.Value)
            : null;

        FormSubmission submission = FormSubmission.Create(
            @event.FormDefinitionId,
            @event.OrganizationId,
            @event.ExecutionId,
            @event.ExecutionStepId,
            assigneeUserId,
            assigneeRoleId: null,
            expiresAt,
            createdBy: "workflow-engine");

        await submissionRepo.AddAsync(submission, ct);
        await uow.SaveChangesAsync(ct);

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
            @event.ExecutionId);
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
