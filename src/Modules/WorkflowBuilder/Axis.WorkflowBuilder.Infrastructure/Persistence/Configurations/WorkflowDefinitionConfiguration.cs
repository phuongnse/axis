using System.Text.Json;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.ValueObjects;
using Axis.WorkflowBuilder.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    private static readonly ValueConverter<List<WorkflowStep>, string> StepsConverter = new(
        steps => JsonSerializer.Serialize(steps, WorkflowJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<WorkflowStep>>(json, WorkflowJsonOptions.Options) ?? new List<WorkflowStep>());

    private static readonly ValueComparer<List<WorkflowStep>> StepsComparer = new(
        (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
        l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        l => l.ToList());

    private static readonly ValueConverter<List<StepTransition>, string> TransitionsConverter = new(
        transitions => JsonSerializer.Serialize(transitions, WorkflowJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<StepTransition>>(json, WorkflowJsonOptions.Options) ?? new List<StepTransition>());

    private static readonly ValueComparer<List<StepTransition>> TransitionsComparer = new(
        (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
        l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        l => l.ToList());

    private static readonly ValueConverter<List<WorkflowTrigger>, string> TriggersConverter = new(
        triggers => JsonSerializer.Serialize(triggers, WorkflowJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<WorkflowTrigger>>(json, WorkflowJsonOptions.Options) ?? new List<WorkflowTrigger>());

    private static readonly ValueComparer<List<WorkflowTrigger>> TriggersComparer = new(
        (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
        l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        l => l.ToList());

    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");

        builder.Property(w => w.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(w => w.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(w => w.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(w => w.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property<List<WorkflowStep>>("_steps")
            .HasField("_steps")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("steps")
            .HasColumnType("jsonb")
            .HasConversion(StepsConverter, StepsComparer)
            .IsRequired();

        builder.Property<List<StepTransition>>("_transitions")
            .HasField("_transitions")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("transitions")
            .HasColumnType("jsonb")
            .HasConversion(TransitionsConverter, TransitionsComparer)
            .IsRequired();

        builder.Property<List<WorkflowTrigger>>("_triggers")
            .HasField("_triggers")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("triggers")
            .HasColumnType("jsonb")
            .HasConversion(TriggersConverter, TriggersComparer)
            .IsRequired();

        builder.Ignore(w => w.Steps);
        builder.Ignore(w => w.Transitions);
        builder.Ignore(w => w.Triggers);

        builder.HasIndex(w => new { w.OrganizationId, w.Name });
    }
}
