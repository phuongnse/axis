namespace Axis.Identity.Application.Repositories;

/// <summary>US-001: caller-supplied idempotency key for registration deduplication.</summary>
public interface IRegistrationIdempotencyRepository
{
    Task<RegistrationIdempotencyAcquireResult> AcquireAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
