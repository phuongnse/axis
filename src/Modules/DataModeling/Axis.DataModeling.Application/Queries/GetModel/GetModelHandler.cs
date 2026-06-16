using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Queries.GetModel;

/// <summary>/032: Loads a model with its complete field list.</summary>
public sealed class GetModelHandler(IDataModelRepository modelRepo)
    : IQueryHandler<GetModelQuery, Result<ModelDetailDto>>
{
    public async Task<Result<ModelDetailDto>> Handle(GetModelQuery query, CancellationToken cancellationToken)
    {
        DataModel? model = await modelRepo.GetByIdAsync(query.ModelId, query.tenantId, cancellationToken);
        if (model is null)
            return Result.Failure<ModelDetailDto>(ErrorCodes.NotFound, "Model not found.");

        return new ModelDetailDto(
            model.Id,
            model.Name,
            model.Description,
            model.Icon,
            model.Color,
            model.CreatedAt,
            model.UpdatedAt,
            model.Fields
                .OrderBy(f => f.IsSystem ? int.MaxValue : f.DisplayOrder)
                .Select(f => new FieldDefinitionDto(
                    f.Id, f.Name, f.Label, f.HelpText,
                    f.Type.ToString(), f.IsRequired, f.IsSystem, f.DisplayOrder, f.Config))
                .ToList()
                .AsReadOnly());
    }
}
