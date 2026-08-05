using Axis.Rules.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Rules.Infrastructure.Persistence.Configurations;

internal sealed class RuleDefinitionVersionConfiguration : IEntityTypeConfiguration<RuleDefinitionVersion>
{
    private static readonly ValueConverter<List<RuleInputDefinition>, string> InputsConverter =
        new(
            value => RulePersistenceJson.SerializeInputs(value),
            value => RulePersistenceJson.DeserializeInputs(value));

    private static readonly ValueComparer<List<RuleInputDefinition>> InputsComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeInputs(left ?? new List<RuleInputDefinition>()) == RulePersistenceJson.SerializeInputs(right ?? new List<RuleInputDefinition>()),
            value => RulePersistenceJson.SerializeInputs(value ?? new List<RuleInputDefinition>()).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeInputs(RulePersistenceJson.SerializeInputs(value ?? new List<RuleInputDefinition>())));

    private static readonly ValueConverter<RuleConditionNode, string> ConditionConverter =
        new(
            value => RulePersistenceJson.SerializeCondition(value),
            value => RulePersistenceJson.DeserializeCondition(value)!);

    private static readonly ValueComparer<RuleConditionNode> ConditionComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeCondition(left) == RulePersistenceJson.SerializeCondition(right),
            value => RulePersistenceJson.SerializeCondition(value).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeCondition(RulePersistenceJson.SerializeCondition(value))!);

    private static readonly ValueConverter<RuleOutputContract, string> OutputConverter =
        new(
            value => RulePersistenceJson.SerializeOutput(value),
            value => RulePersistenceJson.DeserializeOutput(value));

    private static readonly ValueComparer<RuleOutputContract> OutputComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeOutput(left!) == RulePersistenceJson.SerializeOutput(right!),
            value => RulePersistenceJson.SerializeOutput(value!).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeOutput(RulePersistenceJson.SerializeOutput(value!)));

    public void Configure(EntityTypeBuilder<RuleDefinitionVersion> builder)
    {
        builder.ToTable("rule_definition_versions");
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).HasColumnName("id").HasConversion(RuleValueConverters.DefinitionVersionId).ValueGeneratedNever();
        builder.Property(version => version.DefinitionId).HasColumnName("rule_definition_id").HasConversion(RuleValueConverters.DefinitionId).IsRequired();
        builder.Property(version => version.Version).HasColumnName("version_number").IsRequired();
        builder.Property(version => version.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(version => version.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        builder.Property(version => version.ExpressionLanguageVersion).HasColumnName("expression_language_version").IsRequired();
        builder.Ignore(version => version.Inputs);
        builder.Property<List<RuleInputDefinition>>("_inputs")
            .HasColumnName("inputs")
            .HasColumnType("jsonb")
            .HasConversion(InputsConverter)
            .IsRequired()
            .Metadata.SetValueComparer(InputsComparer);
        builder.Property(version => version.Condition)
            .HasColumnName("condition")
            .HasColumnType("jsonb")
            .HasConversion(ConditionConverter)
            .IsRequired()
            .Metadata.SetValueComparer(ConditionComparer);
        builder.Property(version => version.Output)
            .HasColumnName("output")
            .HasColumnType("jsonb")
            .HasConversion(OutputConverter)
            .IsRequired()
            .Metadata.SetValueComparer(OutputComparer);
        builder.Property(version => version.PublishedByUserId).HasColumnName("published_by_user_id").IsRequired();
        builder.Property(version => version.PublishedAt).HasColumnName("published_at").IsRequired();
        builder.HasIndex(version => new { version.DefinitionId, version.Version }).IsUnique();

        foreach (IMutableProperty property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
