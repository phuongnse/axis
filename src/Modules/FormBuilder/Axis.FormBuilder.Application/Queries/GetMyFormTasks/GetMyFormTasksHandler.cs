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
            ? await submissionRepo.GetPendingForUserAsync(query.UserId, query.tenantId, cancellationToken)
            : await submissionRepo.GetByUserAndStatusAsync(
                query.UserId, query.tenantId, query.Status, cancellationToken);

        List<FormTaskSummaryDto> results = new(submissions.Count);
        Dictionary<Guid, string> formNameCache = new();

        foreach (FormSubmission submission in submissions)
        {
            Guid formDefinitionId = submission.FormDefinitionId;
            if (!formNameCache.ContainsKey(formDefinitionId))
            {
                FormDefinition? form = await formRepo.GetByIdAsync(
                    formDefinitionId, submission.tenantId, cancellationToken);
                formNameCache[formDefinitionId] = form?.Name ?? "Unknown form";
            }

            string formName = formNameCache[formDefinitionId];

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
