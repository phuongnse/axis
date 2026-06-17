using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Repositories;

internal sealed class FormModelReferenceRepository(FormBuilderDbContext context) : IFormModelReferenceRepository
{
    public async Task<IReadOnlySet<Guid>> GetBrokenFieldIdsForFormAsync(Guid formId, CancellationToken ct = default)
    {
        List<Guid> ids = await context.FormModelReferences
            .Where(r => r.FormId == formId && r.IsBroken)
            .Select(r => r.FormFieldId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<int> CountActiveReferencesToModelAsync(
        Guid modelId,
        Guid workspaceId,
        CancellationToken ct = default)
        => await context.FormModelReferences
            .Where(r => r.ModelId == modelId && r.workspaceId == workspaceId && !r.IsBroken)
            .CountAsync(ct);
}
