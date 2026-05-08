namespace Axis.WorkflowEngine.Application.Services;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
