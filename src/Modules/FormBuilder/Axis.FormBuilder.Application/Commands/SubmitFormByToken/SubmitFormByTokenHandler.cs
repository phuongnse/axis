using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.SubmitFormByToken;

public sealed class SubmitFormByTokenHandler(
    IFormSubmissionRepository submissionRepo,
    IUnitOfWork uow)
    : ICommandHandler<SubmitFormByTokenCommand>
{
    public async Task<Result> Handle(SubmitFormByTokenCommand command, CancellationToken cancellationToken)
    {
        FormSubmission? submission = await submissionRepo.GetByAccessTokenAsync(command.AccessToken, cancellationToken);
        if (submission is null)
            return Result.Failure(ErrorCodes.NotFound, "Form task not found.");

        if (submission.Status == FormSubmissionStatus.Submitted)
            return Result.Failure(ErrorCodes.BusinessRule, "This form has already been submitted.");

        if (submission.Status == FormSubmissionStatus.Expired
            || (submission.ExpiresAt.HasValue && submission.ExpiresAt.Value < DateTimeOffset.UtcNow))
        {
            if (submission.Status == FormSubmissionStatus.Pending && submission.ExpiresAt.HasValue
                && submission.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                try
                {
                    submission.Expire();
                    await uow.SaveChangesAsync(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // Already expired by another request
                }
            }

            return Result.Failure(ErrorCodes.BusinessRule,
                "This form request has expired. Contact your workflow administrator.");
        }

        if (submission.Status == FormSubmissionStatus.Cancelled)
            return Result.Failure(ErrorCodes.BusinessRule,
                "This workflow has been cancelled.");

        Guid submittedBy = command.SubmittedByUserId
            ?? submission.AssigneeUserId
            ?? Guid.Empty;

        try
        {
            submission.Submit(submittedBy, command.Data);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
