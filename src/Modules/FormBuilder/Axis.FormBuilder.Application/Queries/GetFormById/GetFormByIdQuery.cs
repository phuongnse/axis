using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Queries.GetFormById;

public sealed record GetFormByIdQuery(Guid FormId, Guid workspaceId) : IQuery<FormDetailDto?>;
