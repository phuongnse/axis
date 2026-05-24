using axis.datamodeling.events;
using Axis.DataModeling.Contracts;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Shared.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.FormBuilder.Infrastructure.Handlers;

/// <summary>
/// Flags Relation Picker fields as broken when their target model is deleted (E03 US-033, E05).
/// </summary>
internal sealed class ModelDeletedHandler(
    IConfiguration configuration,
    ILogger<ModelDeletedHandler> logger)
{
    public async Task Handle(ModelDeletedEvent @event, CancellationToken cancellationToken)
    {
        Guid organizationId = @event.OrganizationId();
        Guid modelId = @event.ModelId();

        await using FormBuilderDbContext context = CreateTenantContext(organizationId);

        List<FormDefinition> forms = await context.FormDefinitions
            .Where(f => f.DeletedAt == null)
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
                        FormModelReference.Create(form.Id, field.Id, modelId, organizationId, isBroken: true));
                }
                else
                    existing.MarkBroken();

                flagged++;
            }
        }

        if (flagged > 0)
            await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "ModelDeletedHandler: flagged {Count} Relation Picker field(s) for deleted model {ModelId} org {OrganizationId}",
            flagged, modelId, organizationId);
    }

    private FormBuilderDbContext CreateTenantContext(Guid organizationId)
    {
        string connectionString = configuration.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("Missing connection string 'FormBuilder'.");
        DbContextOptionsBuilder<FormBuilderDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        return new FormBuilderDbContext(optionsBuilder.Options, new FixedTenantContext(organizationId));
    }
}
