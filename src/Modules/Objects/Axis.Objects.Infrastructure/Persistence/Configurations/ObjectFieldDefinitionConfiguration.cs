using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal sealed class ObjectFieldDefinitionConfiguration : IEntityTypeConfiguration<ObjectFieldDefinition>
{
    public void Configure(EntityTypeBuilder<ObjectFieldDefinition> builder)
    {
        builder.ToTable("object_definition_fields");
        builder.HasKey(field => field.Id);

        builder.Property(field => field.Id)
            .HasColumnName("id")
            .HasConversion(ObjectValueConverters.FieldDefinitionId)
            .ValueGeneratedNever();

        builder.Property<ObjectDefinitionId>("ObjectDefinitionId")
            .HasColumnName("object_definition_id")
            .HasConversion(ObjectValueConverters.DefinitionId)
            .IsRequired();

        builder.Property(field => field.Key)
            .HasColumnName("field_key")
            .HasMaxLength(63)
            .HasConversion(ObjectValueConverters.FieldKey)
            .IsRequired();

        builder.Property(field => field.Label)
            .HasColumnName("label")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(field => field.Order)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.HasIndex("ObjectDefinitionId", nameof(ObjectFieldDefinition.Key))
            .IsUnique();

        builder.HasIndex("ObjectDefinitionId", nameof(ObjectFieldDefinition.Order));
    }
}
