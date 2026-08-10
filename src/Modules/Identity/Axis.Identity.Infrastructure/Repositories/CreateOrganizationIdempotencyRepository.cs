using System.Security.Cryptography;
using System.Text;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class CreateOrganizationIdempotencyRepository(IdentityDbContext context)
    : ICreateOrganizationIdempotencyRepository
{
    public async Task<CreateOrganizationIdempotencyRecord?> GetAsync(
        Guid userId,
        string key,
        CancellationToken ct = default)
    {
        string scopedKey = CreateScopedKey(userId, key);
        CreateOrganizationIdempotencyRecordEntity? record = await context
            .CreateOrganizationIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScopedKey == scopedKey, ct);

        return record is null
            ? null
            : new(key, record.CanonicalRequest, record.OrganizationId, record.WorkspaceId);
    }

    public async Task AddAsync(
        Guid userId,
        CreateOrganizationIdempotencyRecord record,
        CancellationToken ct = default) =>
        await context.CreateOrganizationIdempotencyRecords.AddAsync(
            new()
            {
                ScopedKey = CreateScopedKey(userId, record.Key),
                CanonicalRequest = record.CanonicalRequest,
                OrganizationId = record.OrganizationId,
                WorkspaceId = record.WorkspaceId,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ct);

    internal static string CreateScopedKey(Guid userId, string key)
    {
        byte[] input = Encoding.UTF8.GetBytes($"{userId:N}:{key}");
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
