using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.BusinessObjects.Infrastructure.Repositories;

internal sealed class BusinessObjectRecordRepository(BusinessObjectsDbContext context)
    : IBusinessObjectRecordRepository
{
    public async Task AddAsync(
        BusinessObjectRecord record,
        CancellationToken cancellationToken = default) =>
        await context.BusinessObjectRecords.AddAsync(record, cancellationToken);

    public Task<BusinessObjectRecord?> GetByIdForWorkspaceAsync(
        BusinessObjectRecordId id,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        context.BusinessObjectRecords.FirstOrDefaultAsync(
            record => record.Id == id && record.WorkspaceId == workspaceId,
            cancellationToken);

    public Task<BusinessObjectRecord?> FindByIdempotencyKeyAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey objectKey,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        context.BusinessObjectRecords.FirstOrDefaultAsync(
            record => record.WorkspaceId == workspaceId &&
                record.ObjectKey == objectKey &&
                record.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<IReadOnlyList<BusinessObjectRecord>> ListForWorkspaceAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey? objectKey,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<BusinessObjectRecord> query = context.BusinessObjectRecords
            .AsNoTracking()
            .Where(record => record.WorkspaceId == workspaceId);
        if (objectKey is BusinessObjectDefinitionKey key)
            query = query.Where(record => record.ObjectKey == key);

        return await query
            .OrderByDescending(record => record.UpdatedAt)
            .ThenBy(record => record.ObjectKey)
            .ThenBy(record => record.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountForWorkspaceAsync(
        Guid workspaceId,
        BusinessObjectDefinitionKey? objectKey,
        CancellationToken cancellationToken = default)
    {
        IQueryable<BusinessObjectRecord> query = context.BusinessObjectRecords
            .AsNoTracking()
            .Where(record => record.WorkspaceId == workspaceId);
        if (objectKey is BusinessObjectDefinitionKey key)
            query = query.Where(record => record.ObjectKey == key);
        return query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessObjectRecord>> ListOwnedForWorkspaceAsync(
        Guid workspaceId,
        SubjectReference owner,
        BusinessObjectDefinitionKey? objectKey,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<BusinessObjectRecord> query = OwnedQuery(workspaceId, owner, objectKey);
        return await query
            .OrderByDescending(record => record.UpdatedAt)
            .ThenBy(record => record.ObjectKey)
            .ThenBy(record => record.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountOwnedForWorkspaceAsync(
        Guid workspaceId,
        SubjectReference owner,
        BusinessObjectDefinitionKey? objectKey,
        CancellationToken cancellationToken = default) =>
        OwnedQuery(workspaceId, owner, objectKey).CountAsync(cancellationToken);

    private IQueryable<BusinessObjectRecord> OwnedQuery(
        Guid workspaceId,
        SubjectReference owner,
        BusinessObjectDefinitionKey? objectKey)
    {
        IQueryable<BusinessObjectRecord> query = context.BusinessObjectRecords
            .AsNoTracking()
            .Where(record => record.WorkspaceId == workspaceId
                && record.Owner.Kind == owner.Kind
                && record.Owner.Id == owner.Id);
        return objectKey is BusinessObjectDefinitionKey key
            ? query.Where(record => record.ObjectKey == key)
            : query;
    }
}
