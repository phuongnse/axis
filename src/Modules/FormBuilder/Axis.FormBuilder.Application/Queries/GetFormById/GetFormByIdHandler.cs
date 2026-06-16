using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormById;

public sealed class GetFormByIdHandler(
    IFormRepository formRepo,
    IFormModelReferenceRepository formModelReferenceRepo)
    : IQueryHandler<GetFormByIdQuery, FormDetailDto?>
{
    public async Task<FormDetailDto?> Handle(GetFormByIdQuery query, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(query.FormId, query.TeamAccountId, cancellationToken);
        if (form is null) return null;

        IReadOnlySet<Guid> brokenFieldIds = await formModelReferenceRepo.GetBrokenFieldIdsForFormAsync(
            query.FormId, cancellationToken);

        IReadOnlyList<FormFieldDto> fields = form.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new FormFieldDto(
                f.Id, f.Key, f.Label, f.Type, f.IsRequired, f.DisplayOrder, f.Config,
                brokenFieldIds.Contains(f.Id)))
            .ToList();

        return new FormDetailDto(
            form.Id, form.Name, form.Description,
            form.CreatedBy, form.CreatedAt, form.UpdatedAt,
            fields);
    }
}
