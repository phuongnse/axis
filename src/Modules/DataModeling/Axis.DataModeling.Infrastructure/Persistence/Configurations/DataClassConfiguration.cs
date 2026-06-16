using System.Text.Json;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.DataModeling.Infrastructure.Persistence.Configurations;

internal sealed class DataClassConfiguration : IEntityTypeConfiguration<DataClass>
{
    private static readonly ValueConverter<List<FieldDefinition>, string> FieldsConverter = new(
        fields => JsonSerializer.Serialize(fields, FieldJsonOptions.Options),
        json => JsonSerializer.Deserialize<List<FieldDefinition>>(json, FieldJsonOptions.Options) ?? new List<FieldDefinition>());

    private static readonly ValueComparer<List<FieldDefinition>> FieldsComparer = new(
        (l1, l2) => l1 != null && l2 != null && l1.SequenceEqual(l2),
        l => l.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
        l => l.ToList());

    public void Configure(EntityTypeBuilder<DataClass> builder)
    {
        builder.ToTable("data_classes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(c => c.tenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(c => c.DeletedAt)
            .HasColumnName("deleted_at");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(c => c.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(100)
            .IsRequired();

        builder.HasQueryFilter(c => !c.DeletedAt.HasValue);

        builder.Property<List<FieldDefinition>>("_fields")
            .HasField("_fields")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("fields")
            .HasColumnType("jsonb")
            .HasConversion(FieldsConverter, FieldsComparer)
            .IsRequired();

        builder.Ignore(c => c.Fields);

        builder.HasIndex(c => new { c.tenantId, c.Name });
    }
}
