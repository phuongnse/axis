using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;

namespace Axis.Objects.Application.Repositories;

public interface IObjectDefinitionRepository
{
    Task AddAsync(ObjectDefinition definition, CancellationToken ct = default);
    Task<ObjectDefinition?> GetByIdForWorkspaceAsync(ObjectDefinitionId id, Guid workspaceId, CancellationToken ct = default);
    Task<bool> ObjectKeyExistsAsync(Guid workspaceId, ObjectDefinitionKey key, ObjectDefinitionId? exceptId = null, CancellationToken ct = default);
    Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<ObjectDefinition>> ListForWorkspaceAsync(Guid workspaceId, int page, int pageSize, CancellationToken ct = default);
}
