using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectDefinitionVersionChoiceOptionConfiguration
    : IEntityTypeConfiguration<BusinessObjectDefinitionVersionChoiceOption>
{
    public void Configure(EntityTypeBuilder<BusinessObjectDefinitionVersionChoiceOption> builder)
    {
        builder.ToTable("business_object_definition_version_field_choice_options");
        builder.HasKey(option => option.Id);

        builder.Property(option => option.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionChoiceOptionId)
            .ValueGeneratedNever();
        builder.Property<BusinessObjectDefinitionVersionFieldId>("BusinessObjectDefinitionVersionFieldId")
            .HasColumnName("business_object_definition_version_field_id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionFieldId)
            .IsRequired();
        builder.Property(option => option.SourceChoiceOptionId)
            .HasColumnName("source_choice_option_id")
            .HasConversion(BusinessObjectValueConverters.ChoiceOptionId)
            .IsRequired();
        BusinessObjectChoiceOptionConfiguration.ConfigureOptionColumns(builder);

        builder.HasIndex("BusinessObjectDefinitionVersionFieldId", nameof(BusinessObjectDefinitionVersionChoiceOption.Key))
            .IsUnique();
        builder.HasIndex("BusinessObjectDefinitionVersionFieldId", nameof(BusinessObjectDefinitionVersionChoiceOption.Order))
            .IsUnique();
        builder.HasIndex("BusinessObjectDefinitionVersionFieldId", nameof(BusinessObjectDefinitionVersionChoiceOption.SourceChoiceOptionId))
            .IsUnique();
    }
}
