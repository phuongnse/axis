using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectFieldRuleConfiguration : IEntityTypeConfiguration<BusinessObjectFieldRule>
{
    public void Configure(EntityTypeBuilder<BusinessObjectFieldRule> builder)
    {
        builder.ToTable("business_object_definition_field_rules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.FieldRuleId)
            .ValueGeneratedNever();

        builder.Property<BusinessObjectFieldDefinitionId>("BusinessObjectFieldDefinitionId")
            .HasColumnName("business_object_field_definition_id")
            .HasConversion(BusinessObjectValueConverters.FieldDefinitionId)
            .IsRequired();

        ConfigureRuleColumns(builder);

        builder.HasIndex("BusinessObjectFieldDefinitionId", nameof(BusinessObjectFieldRule.BindingId))
            .IsUnique();

        builder.HasIndex("BusinessObjectFieldDefinitionId", nameof(BusinessObjectFieldRule.Order));
    }

    internal static void ConfigureRuleColumns<TRule>(EntityTypeBuilder<TRule> builder)
        where TRule : class
    {
        builder.Property<Guid>(nameof(BusinessObjectFieldRule.BindingId))
            .HasColumnName("binding_id")
            .IsRequired();

        builder.Property<int>(nameof(BusinessObjectFieldRule.BindingRevision))
            .HasColumnName("binding_revision")
            .IsRequired();

        builder.Property<int>(nameof(BusinessObjectFieldRule.Order))
            .HasColumnName("sort_order")
            .IsRequired();

    }
}
