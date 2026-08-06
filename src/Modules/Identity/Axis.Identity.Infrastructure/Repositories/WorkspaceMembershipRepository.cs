using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceMembershipRepository(IdentityDbContext context) : IWorkspaceMembershipRepository { public async Task AddAsync(WorkspaceMembership membership, CancellationToken ct = default) => await context.WorkspaceMemberships.AddAsync(membership, ct); public Task<WorkspaceMembership?> GetActiveAsync(Guid workspaceId, Guid userId, CancellationToken ct = default) => context.WorkspaceMemberships.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId && x.Status == MembershipStatus.Active, ct); public async Task<IReadOnlyList<WorkspaceMembership>> ListActiveForUserAsync(Guid userId, CancellationToken ct = default) => await context.WorkspaceMemberships.Where(x => x.UserId == userId && x.Status == MembershipStatus.Active).OrderBy(x => x.WorkspaceId).ToListAsync(ct); }
