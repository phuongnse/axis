using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal sealed class ObjectDefinitionVersionFieldConfiguration : IEntityTypeConfiguration<ObjectDefinitionVersionField>
{
    public void Configure(EntityTypeBuilder<ObjectDefinitionVersionField> builder)
    {
        builder.ToTable("object_definition_version_fields");
        builder.HasKey(field => field.Id);

        builder.Property(field => field.Id)
            .HasColumnName("id")
            .HasConversion(ObjectValueConverters.FieldDefinitionId)
            .ValueGeneratedNever();

        builder.Property<ObjectDefinitionVersionId>("ObjectDefinitionVersionId")
            .HasColumnName("object_definition_version_id")
            .HasConversion(ObjectValueConverters.DefinitionVersionId)
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

        builder.Property(field => field.FieldType)
            .HasColumnName("field_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex("ObjectDefinitionVersionId", nameof(ObjectDefinitionVersionField.Key))
            .IsUnique();

        builder.HasIndex("ObjectDefinitionVersionId", nameof(ObjectDefinitionVersionField.Order));

        builder.HasMany(field => field.Rules)
            .WithOne()
            .HasForeignKey("ObjectDefinitionVersionFieldId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(field => field.Rules)
            .HasField("_rules")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
