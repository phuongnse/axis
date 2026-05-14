using Axis.DataModeling.Infrastructure.Persistence;
using IDataModelingUnitOfWork = Axis.DataModeling.Application.Services.IUnitOfWork;

namespace Axis.Api.Tests.Helpers;

internal sealed class NullDataModelingUnitOfWork(DataModelingDbContext context)
    : IDataModelingUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
