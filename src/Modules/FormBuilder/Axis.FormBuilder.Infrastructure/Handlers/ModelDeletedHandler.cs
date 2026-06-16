using axis.datamodeling.events;
using Axis.DataModeling.Contracts;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.FormBuilder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

/// <summary>
/// Flags Relation Picker fields as broken when their target model is deleted.
/// </summary>
internal sealed class ModelDeletedHandler(
    FormBuilderDbContext context,
    IUnitOfWork uow,
    ILogger<ModelDeletedHandler> logger)
{
    public async Task Handle(ModelDeletedEvent @event, CancellationToken cancellationToken)
    {
        Guid teamAccountId = @event.TeamAccountId();
        Guid modelId = @event.ModelId();

        List<FormDefinition> forms = await context.FormDefinitions
            .Where(f => f.DeletedAt == null && f.TeamAccountId == teamAccountId)
            .ToListAsync(cancellationToken);

        int flagged = 0;
        foreach (FormDefinition form in forms)
        {
            foreach (FormField field in form.Fields)
            {
                if (field.Type != FormFieldType.RelationPicker || field.Config is not RelationPickerFieldConfig relation)
                    continue;
                if (relation.TargetModelId != modelId)
                    continue;

                FormModelReference? existing = await context.FormModelReferences
                    .FirstOrDefaultAsync(
                        r => r.FormId == form.Id && r.FormFieldId == field.Id,
                        cancellationToken);

                if (existing is null)
                {
                    context.FormModelReferences.Add(
                        FormModelReference.Create(form.Id, field.Id, modelId, teamAccountId, isBroken: true));
                }
                else
                    existing.MarkBroken();

                flagged++;
            }
        }

        if (flagged > 0)
            await uow.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "ModelDeletedHandler: flagged {Count} Relation Picker field(s) for deleted model {ModelId} team account {TeamAccountId}",
            flagged, modelId, teamAccountId);
    }
}
