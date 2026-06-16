using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Services;

/// <summary>
/// Cross-module guard that blocks form deletion when referenced by active workflow steps.
/// Implemented in FormBuilder.Infrastructure via WorkflowBuilder gRPC (ADR-014).
/// Workspace scope is derived from the caller's JWT on the gRPC server side.
/// </summary>
public interface IFormDeletionGuard
{
    Task<Result> ValidateCanDeleteAsync(
        Guid formId,
        CancellationToken cancellationToken = default);
}
