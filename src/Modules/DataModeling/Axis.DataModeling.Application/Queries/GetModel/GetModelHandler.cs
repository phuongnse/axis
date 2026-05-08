using Axis.DataModeling.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Queries.GetModel;

/// <summary>US-031/032: Loads a model with its complete field list.</summary>
public sealed class GetModelHandler(IDataModelRepository modelRepo)
    : IQueryHandler<GetModelQuery, ModelDetailDto?>
{
    public async Task<ModelDetailDto?> Handle(GetModelQuery query, CancellationToken cancellationToken)
    {
        var model = await modelRepo.GetByIdAsync(query.ModelId, query.OrganizationId, cancellationToken);
        if (model is null) return null;

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
