namespace Axis.Identity.Application.Repositories;

/// <summary>US-001: caller-supplied idempotency key for registration deduplication.</summary>
public interface IRegistrationIdempotencyRepository
{
    /// <summary>Attempts to claim the key; returns false when another request already claimed it.</summary>
    Task<bool> TryClaimAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
