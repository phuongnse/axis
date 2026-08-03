using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;

namespace Axis.BusinessObjects.Application.Repositories;

public interface IBusinessObjectRecordRepository
{
    Task AddAsync(BusinessObjectRecord record, CancellationToken cancellationToken = default);
    Task<BusinessObjectRecord?> GetByIdForWorkspaceAsync(
        BusinessObjectRecordId id,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
    Task<BusinessObjectRecord?> FindByIdempotencyKeyAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey objectKey,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessObjectRecord>> ListForWorkspaceAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey? objectKey,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey? objectKey,
        CancellationToken cancellationToken = default);
}
