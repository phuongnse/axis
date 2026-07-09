using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal sealed class ObjectDefinitionVersionFieldVariantConfiguration
    : IEntityTypeConfiguration<ObjectDefinitionVersionFieldVariant>
{
    public void Configure(EntityTypeBuilder<ObjectDefinitionVersionFieldVariant> builder)
    {
        builder.ToTable("object_definition_version_field_variants");
        builder.HasKey(variant => variant.Id);

        builder.Property(variant => variant.Id)
            .HasColumnName("id")
            .HasConversion(ObjectValueConverters.FieldVariantId)
            .ValueGeneratedNever();

        builder.Property<ObjectFieldDefinitionId>("ObjectDefinitionVersionFieldId")
            .HasColumnName("object_definition_version_field_id")
            .HasConversion(ObjectValueConverters.FieldDefinitionId)
            .IsRequired();

        ObjectFieldVariantConfiguration.ConfigureVariantColumns(builder);

        builder.HasIndex("ObjectDefinitionVersionFieldId", nameof(ObjectDefinitionVersionFieldVariant.Kind))
            .IsUnique();

        builder.HasIndex("ObjectDefinitionVersionFieldId", nameof(ObjectDefinitionVersionFieldVariant.Order));
    }
}
