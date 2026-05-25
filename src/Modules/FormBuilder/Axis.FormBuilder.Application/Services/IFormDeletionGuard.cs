using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Services;

public interface IFormDeletionGuard
{
    Task<Result> ValidateCanDeleteAsync(
        Guid formId,
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
