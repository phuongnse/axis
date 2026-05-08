using System.Text.Json;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.FormBuilder.Infrastructure.Persistence.Configurations;

internal sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    private static readonly ValueConverter<List<FormField>, string> FieldsConverter = new(
        fields => JsonSerializer.Serialize(fields, FormJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<FormField>>(json, FormJsonOptions.Options) ?? new List<FormField>());

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

        builder.Property(f => f.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(f => f.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property<List<FormField>>("_fields")
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(FieldsConverter)
            .IsRequired();

        builder.Ignore(f => f.Fields);

        builder.HasIndex(f => new { f.OrganizationId, f.Name });
    }
}
