using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormPicker;

public sealed record GetFormPickerQuery(Guid OrganizationId)
    : IQuery<IReadOnlyList<GetFormPickerDto>>;
