using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Axis.Objects.Infrastructure.Persistence.Configurations;

internal sealed class ObjectDefinitionVersionFieldRuleConfiguration
    : IEntityTypeConfiguration<ObjectDefinitionVersionFieldRule>
{
    public void Configure(EntityTypeBuilder<ObjectDefinitionVersionFieldRule> builder)
    {
        builder.ToTable("object_definition_version_field_rules");
        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasColumnName("id")
            .HasConversion(ObjectValueConverters.FieldRuleId)
            .ValueGeneratedNever();

        builder.Property<ObjectFieldDefinitionId>("ObjectDefinitionVersionFieldId")
            .HasColumnName("object_definition_version_field_id")
            .HasConversion(ObjectValueConverters.FieldDefinitionId)
            .IsRequired();

        ObjectFieldRuleConfiguration.ConfigureRuleColumns(builder);

        builder.HasIndex("ObjectDefinitionVersionFieldId", nameof(ObjectDefinitionVersionFieldRule.DefinitionKey))
            .IsUnique();

        builder.HasIndex("ObjectDefinitionVersionFieldId", nameof(ObjectDefinitionVersionFieldRule.Order));
    }
}
