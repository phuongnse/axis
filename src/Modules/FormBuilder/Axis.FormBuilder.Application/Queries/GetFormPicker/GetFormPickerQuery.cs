using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormPicker;

public sealed record GetFormPickerQuery(Guid workspaceId)
    : IQuery<IReadOnlyList<GetFormPickerDto>>;
