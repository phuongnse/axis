using Axis.DataModeling.Application.Queries.GetModel;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetDataClass;

/// <summary>Loads a data class with its complete field list.</summary>
public sealed class GetDataClassHandler(IDataClassRepository dataClassRepo)
    : IQueryHandler<GetDataClassQuery, Result<DataClassDetailDto>>
{
    public async Task<Result<DataClassDetailDto>> Handle(GetDataClassQuery query, CancellationToken cancellationToken)
    {
        DataClass? dc = await dataClassRepo.GetByIdAsync(query.DataClassId, query.workspaceId, cancellationToken);
        if (dc is null)
            return Result.Failure<DataClassDetailDto>(ErrorCodes.NotFound, "Data class not found.");

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
