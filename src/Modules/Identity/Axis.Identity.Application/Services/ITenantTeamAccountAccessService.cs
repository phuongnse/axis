using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

/// <summary>
/// Evaluates whether a team account may use tenant-scoped module APIs.
/// Returns <see cref="Result.Success"/> when allowed; <see cref="Result.Failure"/> with
/// <see cref="ErrorCodes.Forbidden"/> and a user-facing detail message otherwise.
/// </summary>
public interface ITenantTeamAccountAccessService
{
    Task<Result> EvaluateAsync(Guid teamAccountId, CancellationToken cancellationToken = default);
}
