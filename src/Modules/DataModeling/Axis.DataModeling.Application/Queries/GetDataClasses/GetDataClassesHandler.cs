using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClasses;

/// <summary>US-037: Lists all non-deleted data classes for the org, sorted by name.</summary>
public sealed class GetDataClassesHandler(IDataClassRepository dataClassRepo)
    : IQueryHandler<GetDataClassesQuery, IReadOnlyList<DataClassSummaryDto>>
{
    public async Task<IReadOnlyList<DataClassSummaryDto>> Handle(
        GetDataClassesQuery query, CancellationToken cancellationToken)
    {
        var classes = await dataClassRepo.GetAllAsync(query.OrganizationId, cancellationToken);

        return classes
            .Select(c => new DataClassSummaryDto(c.Id, c.Name, c.Description, c.Fields.Count, c.CreatedAt))
            .ToList()
            .AsReadOnly();
    }
}
