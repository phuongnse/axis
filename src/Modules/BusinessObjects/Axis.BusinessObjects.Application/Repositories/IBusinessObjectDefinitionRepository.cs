using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Application.Repositories;

public interface IBusinessObjectDefinitionRepository
{
    Task AddAsync(BusinessObjectDefinition definition, CancellationToken ct = default);
    Task<BusinessObjectDefinition?> GetByIdForWorkspaceAsync(BusinessObjectDefinitionId id, Guid workspaceId, CancellationToken ct = default);
    Task<bool> ObjectKeyExistsAsync(Guid workspaceId, BusinessObjectDefinitionKey key, BusinessObjectDefinitionId? exceptId = null, CancellationToken ct = default);
    Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessObjectDefinition>> ListForWorkspaceAsync(Guid workspaceId, int page, int pageSize, CancellationToken ct = default);
}
