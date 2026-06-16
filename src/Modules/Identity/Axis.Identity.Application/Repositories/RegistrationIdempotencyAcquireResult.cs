namespace Axis.Identity.Application.Repositories;

/// <summary>Outcome of claiming an idempotency key for Workspace registration.</summary>
public enum RegistrationIdempotencyAcquireResult
{
    /// <summary>This request owns the key and should execute registration.</summary>
    Acquired,

    /// <summary>A prior request already finished successfully — return the same success response.</summary>
    AlreadyCompleted,

    /// <summary>Another request is in flight with this key — deduplicate without re-executing.</summary>
    InProgress,
}
