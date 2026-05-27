using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Infrastructure.Persistence;

namespace Axis.Testing.TestDoubles;

internal sealed class NullFormBuilderUnitOfWork(FormBuilderDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        context.SaveChangesAsync(ct);
}
