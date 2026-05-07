using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModels;

/// <summary>US-031: Lists all non-deleted models for the org.</summary>
public sealed class GetModelsHandler(IDataModelRepository modelRepo)
    : IQueryHandler<GetModelsQuery, IReadOnlyList<ModelSummaryDto>>
{
    public async Task<IReadOnlyList<ModelSummaryDto>> Handle(GetModelsQuery query, CancellationToken cancellationToken)
    {
        var models = await modelRepo.GetAllAsync(query.OrganizationId, cancellationToken);

        return models
            .OrderBy(m => m.Name)
            .Select(m => new ModelSummaryDto(
                m.Id,
                m.Name,
                m.Description,
                m.Icon,
                m.Color,
                m.Fields.Count,
                m.UpdatedAt))
            .ToList()
            .AsReadOnly();
    }
}
