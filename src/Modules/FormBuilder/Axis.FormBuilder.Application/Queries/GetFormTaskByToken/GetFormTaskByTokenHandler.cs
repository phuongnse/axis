using Axis.FormBuilder.Application.Queries.GetFormById;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormTaskByToken;

public sealed class GetFormTaskByTokenHandler(
    IFormSubmissionRepository submissionRepo,
    IFormRepository formRepo)
    : IQueryHandler<GetFormTaskByTokenQuery, FormTaskByTokenDto?>
{
    public async Task<FormTaskByTokenDto?> Handle(
        GetFormTaskByTokenQuery query,
        CancellationToken cancellationToken)
    {
        FormSubmission? submission = await submissionRepo.GetByAccessTokenAsync(query.AccessToken, cancellationToken);
        if (submission is null)
            return null;

        FormDefinition? form = await formRepo.GetByIdAsync(
            submission.FormDefinitionId, submission.tenantId, cancellationToken);
        if (form is null)
            return null;

        List<FormFieldDto> fields = form.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new FormFieldDto(
                f.Id,
                f.Key,
                f.Label,
                f.Type,
                f.IsRequired,
                f.DisplayOrder,
                f.Config))
            .ToList();

        return new FormTaskByTokenDto(
            submission.Id,
            submission.Status.ToString(),
            form.Id,
            form.Name,
            form.Description,
            fields,
            submission.ExpiresAt);
    }
}
