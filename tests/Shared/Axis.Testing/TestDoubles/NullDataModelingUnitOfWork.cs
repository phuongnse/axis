using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Infrastructure.Persistence;

namespace Axis.Testing.TestDoubles;

internal sealed class NullDataModelingUnitOfWork(DataModelingDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
