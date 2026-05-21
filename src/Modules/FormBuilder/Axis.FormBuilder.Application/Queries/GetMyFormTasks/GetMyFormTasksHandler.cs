using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetMyFormTasks;

public sealed class GetMyFormTasksHandler(
    IFormSubmissionRepository submissionRepo,
    IFormRepository formRepo)
    : IQueryHandler<GetMyFormTasksQuery, IReadOnlyList<FormTaskSummaryDto>>
{
    public async Task<IReadOnlyList<FormTaskSummaryDto>> Handle(
        GetMyFormTasksQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FormSubmission> submissions = query.Status == Domain.Enums.FormSubmissionStatus.Pending
            ? await submissionRepo.GetPendingForUserAsync(query.UserId, query.OrganizationId, cancellationToken)
            : await submissionRepo.GetByUserAndStatusAsync(
                query.UserId, query.OrganizationId, query.Status, cancellationToken);

        List<FormTaskSummaryDto> results = new();
        foreach (FormSubmission submission in submissions)
        {
            FormDefinition? form = await formRepo.GetByIdAsync(
                submission.FormDefinitionId, submission.OrganizationId, cancellationToken);
            string formName = form?.Name ?? "Unknown form";
            results.Add(new FormTaskSummaryDto(
                submission.Id,
                submission.FormDefinitionId,
                formName,
                submission.ExecutionId,
                submission.Status.ToString(),
                submission.CreatedAt,
                submission.ExpiresAt,
                submission.AccessToken));
        }

        return results;
    }
}
