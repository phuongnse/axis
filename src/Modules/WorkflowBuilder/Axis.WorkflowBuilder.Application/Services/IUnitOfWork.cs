namespace Axis.WorkflowBuilder.Application.Services;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
