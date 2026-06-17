using System.Text.Json;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.FormBuilder.Infrastructure.Persistence.Configurations;

internal sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    private static readonly ValueConverter<List<FormField>, string> FieldsConverter = new(
        fields => JsonSerializer.Serialize(fields, FormJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<FormField>>(json, FormJsonOptions.Options) ?? new List<FormField>());

    // Snapshot uses deep-copy via serialization so mutations (e.g. DisplayOrder changes)
    // are detected by EF Core's change tracker. Shallow ToList() would share references
    // and miss in-place property mutations.
    private static readonly ValueComparer<List<FormField>> FieldsComparer = new(
        (l1, l2) => JsonSerializer.Serialize(l1, FormJsonOptions.Options)
                 == JsonSerializer.Serialize(l2, FormJsonOptions.Options),
        l => JsonSerializer.Serialize(l, FormJsonOptions.Options).GetHashCode(),
        l => JsonSerializer.Deserialize<List<FormField>>(
            JsonSerializer.Serialize(l, FormJsonOptions.Options),
            FormJsonOptions.Options) ?? new List<FormField>());

    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable("form_definitions");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(f => f.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(f => f.workspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();

        builder.Property(f => f.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(f => f.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasQueryFilter(f => !f.DeletedAt.HasValue);

        builder.Property<List<FormField>>("_fields")
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(FieldsConverter, FieldsComparer)
            .IsRequired();

        builder.Ignore(f => f.Fields);

        builder.HasIndex(f => new { f.workspaceId, f.Name }).IsUnique()
            .HasFilter("deleted_at IS NULL");
    }
}
