namespace Axis.Identity.Application.Repositories;

/// <summary>Persists caller-supplied registration idempotency keys.</summary>
public interface IRegistrationIdempotencyRepository
{
    Task<RegistrationIdempotencyAcquireResult> AcquireAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
