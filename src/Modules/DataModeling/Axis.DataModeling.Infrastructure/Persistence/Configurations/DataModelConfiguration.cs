using System.Text.Json;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.DataModeling.Infrastructure.Persistence.Configurations;

internal sealed class DataModelConfiguration : IEntityTypeConfiguration<DataModel>
{
    private static readonly ValueConverter<List<FieldDefinition>, string> FieldsConverter = new(
        fields => JsonSerializer.Serialize(fields, FieldJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<FieldDefinition>>(json, FieldJsonOptions.Options) ?? new List<FieldDefinition>());

    private static readonly ValueComparer<List<FieldDefinition>> FieldsComparer = new(
        (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
        l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        l => l.ToList());

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

        builder.Property(m => m.tenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(m => m.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(m => m.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(m => m.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasQueryFilter(m => !m.DeletedAt.HasValue);

        builder.Property<List<FieldDefinition>>("_fields")
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(FieldsConverter, FieldsComparer)
            .IsRequired();

        builder.Ignore(m => m.Fields);

        builder.HasIndex(m => new { m.tenantId, m.Name });
    }
}
