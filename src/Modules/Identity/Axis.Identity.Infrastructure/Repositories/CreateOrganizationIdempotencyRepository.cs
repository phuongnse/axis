using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class CreateOrganizationIdempotencyRepository(IdentityDbContext context) : ICreateOrganizationIdempotencyRepository { public async Task<CreateOrganizationIdempotencyRecord?> GetAsync(string key, CancellationToken ct = default) { CreateOrganizationIdempotencyRecordEntity? record = await context.CreateOrganizationIdempotencyRecords.FirstOrDefaultAsync(x => x.Key == key, ct); return record is null ? null : new(record.Key, record.CanonicalRequest, record.OrganizationId, record.WorkspaceId); } public async Task AddAsync(CreateOrganizationIdempotencyRecord record, CancellationToken ct = default) => await context.CreateOrganizationIdempotencyRecords.AddAsync(new() { Key = record.Key, CanonicalRequest = record.CanonicalRequest, OrganizationId = record.OrganizationId, WorkspaceId = record.WorkspaceId, CreatedAt = DateTimeOffset.UtcNow }, ct); }
