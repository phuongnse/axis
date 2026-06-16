using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormPicker;

/// <summary>Returns all forms for the Tenant as a flat list for use in workflow form-step pickers.</summary>
public sealed class GetFormPickerHandler(IFormRepository formRepo)
    : IQueryHandler<GetFormPickerQuery, IReadOnlyList<GetFormPickerDto>>
{
    public async Task<IReadOnlyList<GetFormPickerDto>> Handle(
        GetFormPickerQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<FormDefinition> all = await formRepo.GetAllAsync(query.tenantId, cancellationToken);

        return all
            .OrderBy(f => f.Name)
            .Select(f => new GetFormPickerDto(f.Id, f.Name, f.Fields.Count))
            .ToList();
    }
}
