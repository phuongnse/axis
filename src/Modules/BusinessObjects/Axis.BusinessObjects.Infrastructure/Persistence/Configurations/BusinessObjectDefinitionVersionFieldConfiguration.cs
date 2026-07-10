using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectDefinitionVersionFieldConfiguration : IEntityTypeConfiguration<BusinessObjectDefinitionVersionField>
{
    public void Configure(EntityTypeBuilder<BusinessObjectDefinitionVersionField> builder)
    {
        builder.ToTable("business_object_definition_version_fields");
        builder.HasKey(field => field.Id);

        builder.Property(field => field.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionFieldId)
            .ValueGeneratedNever();

        builder.Property<BusinessObjectDefinitionVersionId>("BusinessObjectDefinitionVersionId")
            .HasColumnName("business_object_definition_version_id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionId)
            .IsRequired();

        builder.Property(field => field.SourceFieldDefinitionId)
            .HasColumnName("source_field_definition_id")
            .HasConversion(BusinessObjectValueConverters.FieldDefinitionId)
            .IsRequired();

        builder.Property(field => field.Key)
            .HasColumnName("field_key")
            .HasMaxLength(63)
            .HasConversion(BusinessObjectValueConverters.FieldKey)
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

        builder.Property(field => field.ChoiceSelectionMode)
            .HasColumnName("choice_selection_mode")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasIndex("BusinessObjectDefinitionVersionId", nameof(BusinessObjectDefinitionVersionField.Key))
            .IsUnique();

        builder.HasIndex("BusinessObjectDefinitionVersionId", nameof(BusinessObjectDefinitionVersionField.Order));

        builder.HasIndex("BusinessObjectDefinitionVersionId", nameof(BusinessObjectDefinitionVersionField.SourceFieldDefinitionId))
            .IsUnique();

        builder.HasMany(field => field.Rules)
            .WithOne()
            .HasForeignKey("BusinessObjectDefinitionVersionFieldId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasMany(field => field.ChoiceOptions)
            .WithOne()
            .HasForeignKey("BusinessObjectDefinitionVersionFieldId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(field => field.Rules)
            .HasField("_rules")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(field => field.ChoiceOptions)
            .HasField("_choiceOptions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
