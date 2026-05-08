using Axis.DataModeling.Application.Queries.GetModel;
using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetDataClass;

/// <summary>US-039: Loads a data class with its complete field list.</summary>
public sealed class GetDataClassHandler(IDataClassRepository dataClassRepo)
    : IQueryHandler<GetDataClassQuery, DataClassDetailDto?>
{
    public async Task<DataClassDetailDto?> Handle(GetDataClassQuery query, CancellationToken cancellationToken)
    {
        var dc = await dataClassRepo.GetByIdAsync(query.DataClassId, query.OrganizationId, cancellationToken);
        if (dc is null) return null;

        return new DataClassDetailDto(
            dc.Id,
            dc.Name,
            dc.Description,
            dc.CreatedAt,
            dc.Fields
                .OrderBy(f => f.DisplayOrder)
                .Select(f => new FieldDefinitionDto(
                    f.Id, f.Name, f.Label, f.HelpText,
                    f.Type.ToString(), f.IsRequired, false, f.DisplayOrder, f.Config))
                .ToList()
                .AsReadOnly());
    }
}
