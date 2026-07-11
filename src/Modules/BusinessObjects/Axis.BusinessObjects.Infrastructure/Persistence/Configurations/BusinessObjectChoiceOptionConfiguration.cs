using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectChoiceOptionConfiguration : IEntityTypeConfiguration<BusinessObjectChoiceOption>
{
    public void Configure(EntityTypeBuilder<BusinessObjectChoiceOption> builder)
    {
        builder.ToTable("business_object_definition_field_choice_options");
        builder.HasKey(option => option.Id);

        builder.Property(option => option.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.ChoiceOptionId)
            .ValueGeneratedNever();
        builder.Property<BusinessObjectFieldDefinitionId>("BusinessObjectFieldDefinitionId")
            .HasColumnName("business_object_field_definition_id")
            .HasConversion(BusinessObjectValueConverters.FieldDefinitionId)
            .IsRequired();
        ConfigureOptionColumns(builder);

        builder.HasIndex("BusinessObjectFieldDefinitionId", nameof(BusinessObjectChoiceOption.Key))
            .IsUnique();
        builder.HasIndex("BusinessObjectFieldDefinitionId", nameof(BusinessObjectChoiceOption.Order))
            .IsUnique();
    }

    internal static void ConfigureOptionColumns<TOption>(EntityTypeBuilder<TOption> builder)
        where TOption : class
    {
        builder.Property<BusinessObjectChoiceOptionKey>(nameof(BusinessObjectChoiceOption.Key))
            .HasColumnName("option_key")
            .HasMaxLength(63)
            .HasConversion(BusinessObjectValueConverters.ChoiceOptionKey)
            .IsRequired();
        builder.Property<string>(nameof(BusinessObjectChoiceOption.Label))
            .HasColumnName("label")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property<int>(nameof(BusinessObjectChoiceOption.Order))
            .HasColumnName("sort_order")
            .IsRequired();
    }
}
