namespace Axis.FormBuilder.Application.Services;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
