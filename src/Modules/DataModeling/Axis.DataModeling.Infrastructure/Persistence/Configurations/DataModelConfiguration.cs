using System.Text.Json;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.DataModeling.Infrastructure.Persistence.Configurations;

internal sealed class DataModelConfiguration : IEntityTypeConfiguration<DataModel>
{
    private static readonly ValueConverter<List<FieldDefinition>, string> FieldsConverter = new(
        fields => JsonSerializer.Serialize(fields, FieldJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<FieldDefinition>>(json, FieldJsonOptions.Options) ?? new List<FieldDefinition>());

    public void Configure(EntityTypeBuilder<DataModel> builder)
    {
        builder.ToTable("data_models");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(m => m.Icon)
            .HasColumnName("icon")
            .HasMaxLength(50);

        builder.Property(m => m.Color)
            .HasColumnName("color")
            .HasMaxLength(50);

        builder.Property(m => m.OrganizationId)
            .HasColumnName("organization_id")
            .IsRequired();

        builder.Property(m => m.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(m => m.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property<List<FieldDefinition>>("_fields")
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(FieldsConverter)
            .IsRequired();

        builder.Ignore(m => m.Fields);

        builder.HasIndex(m => new { m.OrganizationId, m.Name });
    }
}
