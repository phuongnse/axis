using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceRepository(IdentityDbContext context) : IWorkspaceRepository
{
    public async Task AddAsync(Workspace Workspace, CancellationToken ct = default) =>
        await context.Workspaces.AddAsync(Workspace, ct);

    public async Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await context.Workspaces.FindAsync([id], ct);

    public async Task<Workspace?> GetBySlugAsync(WorkspaceSlug slug, CancellationToken ct = default) =>
        await context.Workspaces
            .FirstOrDefaultAsync(o => o.Slug == slug, ct);

    public async Task<bool> SlugExistsAsync(WorkspaceSlug slug, CancellationToken ct = default) =>
        await context.Workspaces.AnyAsync(o => o.Slug == slug, ct);
}
