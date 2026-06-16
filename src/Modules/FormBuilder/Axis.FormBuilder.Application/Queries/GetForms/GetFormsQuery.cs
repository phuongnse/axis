using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetForms;

public sealed record GetFormsQuery(Guid workspaceId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<FormSummaryDto>>;
