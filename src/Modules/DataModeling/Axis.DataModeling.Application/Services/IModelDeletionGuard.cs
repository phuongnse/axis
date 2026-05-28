using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Services;

/// <summary>
/// Cross-module guard for blocks model deletion when referenced by active form fields.
/// Implemented in DataModeling.Infrastructure via FormBuilder gRPC (ADR-014).
/// </summary>
public interface IModelDeletionGuard
{
    Task<Result> ValidateCanDeleteAsync(
        Guid modelId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
