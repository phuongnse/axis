using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

/// <summary>US-002: max 3 verification resends per normalized email per hour.</summary>
public interface IResendVerificationRateLimiter
{
    /// <summary>
    /// Records a resend attempt when under the limit; otherwise returns <see cref="ErrorCodes.RateLimited"/>.
    /// </summary>
    Task<Result> TryRecordResendAsync(string normalizedEmail, CancellationToken cancellationToken = default);
}
