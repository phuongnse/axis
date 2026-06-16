using Axis.FormBuilder.Application.Messages;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Application.Handlers;

/// <summary>Marks a pending form submission as expired (idempotent).</summary>
public sealed class ExpireFormSubmissionHandler(
    IFormSubmissionRepository submissionRepo,
    IUnitOfWork uow,
    ILogger<ExpireFormSubmissionHandler> logger)
{
    public async Task Handle(ExpireFormSubmissionMessage message, CancellationToken cancellationToken)
    {
        FormSubmission? submission = await submissionRepo.GetByIdAsync(
            message.FormSubmissionId, message.OrganizationId, cancellationToken);

        if (submission is null)
        {
            logger.LogWarning(
                "ExpireFormSubmissionHandler: submission {SubmissionId} not found for org {OrgId}",
                message.FormSubmissionId,
                message.OrganizationId);
            return;
        }

        if (submission.Status != FormSubmissionStatus.Pending)
        {
            logger.LogInformation(
                "ExpireFormSubmissionHandler: submission {SubmissionId} already {Status}, skipping",
                message.FormSubmissionId,
                submission.Status);
            return;
        }

        if (submission.ExpiresAt.HasValue && submission.ExpiresAt.Value > DateTimeOffset.UtcNow)
        {
            logger.LogInformation(
                "ExpireFormSubmissionHandler: submission {SubmissionId} not yet due, skipping",
                message.FormSubmissionId);
            return;
        }

        try
        {
            submission.Expire();
        }
        catch (InvalidOperationException)
        {
            logger.LogInformation(
                "ExpireFormSubmissionHandler: submission {SubmissionId} could not be expired, skipping",
                message.FormSubmissionId);
            return;
        }

        await uow.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "ExpireFormSubmissionHandler: expired form submission {SubmissionId} for execution {ExecutionId}",
            submission.Id,
            submission.ExecutionId);
    }
}
