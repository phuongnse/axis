using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class OrganizationMembershipRepository(IdentityDbContext context) : IOrganizationMembershipRepository { public async Task AddAsync(OrganizationMembership membership, CancellationToken ct = default) => await context.OrganizationMemberships.AddAsync(membership, ct); public Task<OrganizationMembership?> GetActiveAsync(Guid organizationId, Guid userId, CancellationToken ct = default) => context.OrganizationMemberships.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId && x.Status == MembershipStatus.Active, ct); }
