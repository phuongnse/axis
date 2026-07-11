using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectDefinitionVersionFieldRuleConfiguration
    : IEntityTypeConfiguration<BusinessObjectDefinitionVersionFieldRule>
{
    public void Configure(EntityTypeBuilder<BusinessObjectDefinitionVersionFieldRule> builder)
    {
        builder.ToTable("business_object_definition_version_field_rules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionFieldRuleId)
            .ValueGeneratedNever();

        builder.Property<BusinessObjectDefinitionVersionFieldId>("BusinessObjectDefinitionVersionFieldId")
            .HasColumnName("business_object_definition_version_field_id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionFieldId)
            .IsRequired();

        builder.Property(rule => rule.SourceFieldRuleId)
            .HasColumnName("source_field_rule_id")
            .HasConversion(BusinessObjectValueConverters.FieldRuleId)
            .IsRequired();

        BusinessObjectFieldRuleConfiguration.ConfigureRuleColumns(builder);

        builder.HasIndex("BusinessObjectDefinitionVersionFieldId", nameof(BusinessObjectDefinitionVersionFieldRule.DefinitionKey))
            .IsUnique();

        builder.HasIndex("BusinessObjectDefinitionVersionFieldId", nameof(BusinessObjectDefinitionVersionFieldRule.Order));
        builder.HasIndex("BusinessObjectDefinitionVersionFieldId", nameof(BusinessObjectDefinitionVersionFieldRule.SourceFieldRuleId))
            .IsUnique();
    }
}
