using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application.Services;

public interface IBusinessObjectDefinitionInputPlanner
{
    Task<Result<IReadOnlyList<BusinessObjectFieldDefinitionSpec>>> PlanAsync(
        Guid workspaceId,
        IReadOnlyList<BusinessObjectFieldDefinitionInput> fields,
        CancellationToken cancellationToken);
}
