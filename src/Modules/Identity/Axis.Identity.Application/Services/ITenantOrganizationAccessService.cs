using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

/// <summary>
/// Evaluates whether an organization may use tenant-scoped module APIs (E01 F03 US-009).
/// Returns <see cref="Result.Success"/> when allowed; <see cref="Result.Failure"/> with
/// <see cref="ErrorCodes.Forbidden"/> and a user-facing detail message otherwise.
/// </summary>
public interface ITenantOrganizationAccessService
{
    Task<Result> EvaluateAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
