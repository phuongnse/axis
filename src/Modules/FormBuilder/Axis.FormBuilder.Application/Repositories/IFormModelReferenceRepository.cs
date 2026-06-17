namespace Axis.FormBuilder.Application.Repositories;

public interface IFormModelReferenceRepository
{
    Task<IReadOnlySet<Guid>> GetBrokenFieldIdsForFormAsync(Guid formId, CancellationToken ct = default);

    Task<int> CountActiveReferencesToModelAsync(Guid modelId, Guid workspaceId, CancellationToken ct = default);
}
