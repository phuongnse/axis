using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.FormBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Axis.FormBuilder.Infrastructure.Services;

internal sealed class FormModelReferenceSync(FormBuilderDbContext context) : IFormModelReferenceSync
{
    public async Task SyncRelationPickerReferencesAsync(FormDefinition form, CancellationToken ct = default)
    {
        List<FormModelReference> existing = await context.FormModelReferences
            .Where(r => r.FormId == form.Id)
            .ToListAsync(ct);

        HashSet<Guid> currentFieldIds = form.Fields
            .Where(f => f.Type == FormFieldType.RelationPicker && f.Config is RelationPickerFieldConfig)
            .Select(f => f.Id)
            .ToHashSet();

        foreach (FormModelReference stale in existing.Where(r => !currentFieldIds.Contains(r.FormFieldId)))
            context.FormModelReferences.Remove(stale);

        foreach (FormField field in form.Fields)
        {
            if (field.Type != FormFieldType.RelationPicker || field.Config is not RelationPickerFieldConfig relation)
                continue;

            FormModelReference? row = existing.FirstOrDefault(r => r.FormFieldId == field.Id);
            if (row is null)
            {
                context.FormModelReferences.Add(
                    FormModelReference.Create(form.Id, field.Id, relation.TargetModelId, form.TeamAccountId));
            }
            else if (row.ModelId != relation.TargetModelId)
                row.Retarget(relation.TargetModelId);
            else
                row.MarkHealthy();
        }
    }
}
