using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Services;

/// <summary>
/// Cross-module guard for US-033: blocks model deletion when referenced by active form fields.
/// Implemented at the API composition root (modulith); not a FormBuilder dependency from DataModeling.
/// </summary>
public interface IModelDeletionGuard
{
    Task<Result> ValidateCanDeleteAsync(
        Guid modelId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
