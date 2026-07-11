using System.Text.Json;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectFieldRuleConfiguration : IEntityTypeConfiguration<BusinessObjectFieldRule>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<Dictionary<string, string[]>, string> ParametersConverter =
        new(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<Dictionary<string, string[]>>(value, JsonOptions)
                ?? new Dictionary<string, string[]>(StringComparer.Ordinal));

    private static readonly ValueComparer<Dictionary<string, string[]>> ParametersComparer =
        new(
            (left, right) => Serialize(left) == Serialize(right),
            value => Serialize(value).GetHashCode(StringComparison.Ordinal),
            value => value.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.Ordinal));

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

        builder.HasIndex("BusinessObjectFieldDefinitionId", nameof(BusinessObjectFieldRule.DefinitionKey))
            .IsUnique();

        builder.HasIndex("BusinessObjectFieldDefinitionId", nameof(BusinessObjectFieldRule.Order));
    }

    internal static void ConfigureRuleColumns<TRule>(EntityTypeBuilder<TRule> builder)
        where TRule : class
    {
        builder.Property<string>(nameof(BusinessObjectFieldRule.DefinitionKey))
            .HasColumnName("definition_key")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property<int>(nameof(BusinessObjectFieldRule.DefinitionVersion))
            .HasColumnName("definition_version")
            .IsRequired();

        builder.Property<int>(nameof(BusinessObjectFieldRule.Order))
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property<Dictionary<string, string[]>>("_parameters")
            .HasColumnName("parameters")
            .HasColumnType("jsonb")
            .HasConversion(ParametersConverter)
            .IsRequired()
            .Metadata.SetValueComparer(ParametersComparer);
    }

    private static string Serialize(Dictionary<string, string[]>? value) =>
        JsonSerializer.Serialize(value ?? new Dictionary<string, string[]>(StringComparer.Ordinal), JsonOptions);
}
