using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;

namespace Axis.Testing.TestDoubles;

/// <summary>
/// Persists EF changes without publishing domain events (deterministic API test setup).
/// </summary>
public sealed class NullIdentityUnitOfWork(IdentityDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
