using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

/// <summary>Limits verification email resends per normalized email.</summary>
public interface IResendVerificationRateLimiter
{
    /// <summary>
    /// Records a resend attempt when under the limit; otherwise returns <see cref="ErrorCodes.RateLimited"/>.
    /// </summary>
    Task<Result> TryRecordResendAsync(string normalizedEmail, CancellationToken cancellationToken = default);
}
