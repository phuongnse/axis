using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;

namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceRepository
{
    Task AddAsync(Workspace Workspace, CancellationToken ct = default);
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Workspace?> GetPersonalByOwnerUserIdAsync(Guid ownerUserId, CancellationToken ct = default);
    Task<Workspace?> GetBySlugAsync(WorkspaceSlug slug, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(WorkspaceSlug slug, CancellationToken ct = default);
}
