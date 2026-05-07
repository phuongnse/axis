namespace Axis.DataModeling.Application.Services;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
