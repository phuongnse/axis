using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

/// <summary>
/// Evaluates whether a workspace may use workspace-scoped module APIs.
/// Returns <see cref="Result.Success"/> when allowed; <see cref="Result.Failure"/> with
/// <see cref="ErrorCodes.Forbidden"/> and a user-facing detail message otherwise.
/// </summary>
public interface IWorkspaceAccessService
{
    Task<Result> EvaluateAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}
