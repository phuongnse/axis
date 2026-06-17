using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Repositories;

internal sealed class FormRepository(FormBuilderDbContext context) : IFormRepository
{
    public async Task AddAsync(FormDefinition form, CancellationToken ct = default)
        => await context.FormDefinitions.AddAsync(form, ct);

    public async Task<FormDefinition?> GetByIdAsync(Guid id, Guid workspaceId, CancellationToken ct = default)
        => await context.FormDefinitions
            .FirstOrDefaultAsync(f => f.Id == id && f.workspaceId == workspaceId, ct);

    public async Task<IReadOnlyList<FormDefinition>> GetAllAsync(Guid workspaceId, CancellationToken ct = default)
        => await context.FormDefinitions
            .Where(f => f.workspaceId == workspaceId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

    public async Task<bool> NameExistsAsync(string name, Guid workspaceId, Guid? excludeId = null, CancellationToken ct = default)
        => await context.FormDefinitions
            .AnyAsync(f => f.workspaceId == workspaceId
                && f.Name.ToLower() == name.ToLower()
                && (excludeId == null || f.Id != excludeId), ct);

    public async Task<bool> IsReferencedByWorkflowAsync(Guid formId, CancellationToken ct = default)
        => await context.FormWorkflowReferences
            .AnyAsync(r => r.FormId == formId && r.IsActive, ct);
}
