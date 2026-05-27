using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullUnitOfWork(IdentityDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
