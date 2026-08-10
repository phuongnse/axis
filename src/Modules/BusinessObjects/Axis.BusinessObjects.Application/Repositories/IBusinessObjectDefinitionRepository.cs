using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Application.Repositories;

public interface IBusinessObjectDefinitionRepository
{
    Task AddAsync(BusinessObjectDefinition definition, CancellationToken ct = default);
    Task<BusinessObjectDefinition?> GetByIdForWorkspaceAsync(BusinessObjectDefinitionId id, Guid workspaceId, CancellationToken ct = default);
    Task<BusinessObjectDefinition?> GetByKeyForWorkspaceAsync(BusinessObjectDefinitionKey key, Guid workspaceId, CancellationToken ct = default);
    Task<BusinessObjectDefinition?> GetInstalledByComponentKeyAsync(
        Guid workspaceId,
        string componentKey,
        CancellationToken ct = default) =>
        Task.FromResult<BusinessObjectDefinition?>(null);
    Task<BusinessObjectDefinitionVersion?> GetPublishedVersionByIdForWorkspaceAsync(
        BusinessObjectDefinitionVersionId id,
        Guid workspaceId,
        CancellationToken ct = default);
    Task<bool> ObjectKeyExistsAsync(Guid workspaceId, BusinessObjectDefinitionKey key, BusinessObjectDefinitionId? exceptId = null, CancellationToken ct = default);
    Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        string? searchQuery = null,
        bool publishedOnly = false,
        CancellationToken ct = default);
    Task<IReadOnlyList<BusinessObjectDefinition>> ListForWorkspaceAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        string? searchQuery = null,
        bool publishedOnly = false,
        CancellationToken ct = default);
}
