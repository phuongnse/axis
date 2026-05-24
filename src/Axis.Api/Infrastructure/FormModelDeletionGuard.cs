using Axis.DataModeling.Application.Services;
using Axis.FormBuilder.Application.Repositories;
using Axis.Shared.Domain.Primitives;

namespace Axis.Api.Infrastructure;

/// <summary>US-033: delegates active form-reference check to FormBuilder read model.</summary>
internal sealed class FormModelDeletionGuard(IFormModelReferenceRepository formModelReferences)
    : IModelDeletionGuard
{
    public async Task<Result> ValidateCanDeleteAsync(
        Guid modelId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        int formReferenceCount = await formModelReferences.CountActiveReferencesToModelAsync(
            modelId, organizationId, cancellationToken);
        if (formReferenceCount > 0)
        {
            return Result.Failure(
                ErrorCodes.Conflict,
                $"This model is used by {formReferenceCount} form(s). Remove those references before deleting.");
        }

        return Result.Success();
    }
}
