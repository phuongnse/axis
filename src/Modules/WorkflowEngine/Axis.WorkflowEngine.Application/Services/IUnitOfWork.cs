namespace Axis.WorkflowEngine.Application.Services;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
