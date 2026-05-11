using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Repositories;

internal sealed class FormRepository(FormBuilderDbContext context) : IFormRepository
{
    public async Task AddAsync(FormDefinition form, CancellationToken ct = default)
        => await context.FormDefinitions.AddAsync(form, ct);

    public async Task<FormDefinition?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.FormDefinitions
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<FormDefinition>> GetAllAsync(Guid organizationId, CancellationToken ct = default)
        => await context.FormDefinitions
            .Where(f => f.OrganizationId == organizationId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

    public async Task<bool> NameExistsAsync(string name, Guid organizationId, Guid? excludeId = null, CancellationToken ct = default)
        => await context.FormDefinitions
            .AnyAsync(f => f.OrganizationId == organizationId
                && f.Name.ToLower() == name.ToLower()
                && (excludeId == null || f.Id != excludeId), ct);

    public async Task<bool> IsReferencedByWorkflowAsync(Guid formId, CancellationToken ct = default)
    {
        string jsonContains = $"[{{\"type\":\"Form\",\"config\":{{\"formId\":\"{formId:D}\"}}}}]";
        int count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT CAST(COUNT(*) AS int) AS \"Value\" FROM workflow_definitions WHERE deleted_at IS NULL AND steps @> {0}::jsonb",
                jsonContains)
            .FirstAsync(ct);
        return count > 0;
    }
}
