using Axis.Rules.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Rules.Infrastructure.Persistence.Configurations;

internal sealed class RuleDefinitionVersionConfiguration : IEntityTypeConfiguration<RuleDefinitionVersion>
{
    private static readonly ValueConverter<List<RuleParameterDefinition>, string> ParametersConverter =
        new(
            value => RulePersistenceJson.SerializeParameters(value),
            value => RulePersistenceJson.DeserializeParameters(value));

    private static readonly ValueComparer<List<RuleParameterDefinition>> ParametersComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeParameters(left ?? new List<RuleParameterDefinition>())
                == RulePersistenceJson.SerializeParameters(right ?? new List<RuleParameterDefinition>()),
            value => RulePersistenceJson.SerializeParameters(value ?? new List<RuleParameterDefinition>())
                .GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeParameters(
                RulePersistenceJson.SerializeParameters(value ?? new List<RuleParameterDefinition>())));

    private static readonly ValueConverter<RuleConditionNode, string> ConditionConverter =
        new(
            value => RulePersistenceJson.SerializeCondition(value),
            value => RulePersistenceJson.DeserializeCondition(value)!);

    private static readonly ValueComparer<RuleConditionNode> ConditionComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeCondition(left)
                == RulePersistenceJson.SerializeCondition(right),
            value => RulePersistenceJson.SerializeCondition(value).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeCondition(RulePersistenceJson.SerializeCondition(value))!);

    private static readonly ValueConverter<RuleOutcome, string> OutcomeConverter =
        new(
            value => RulePersistenceJson.SerializeOutcome(value),
            value => RulePersistenceJson.DeserializeOutcome(value)!);

    private static readonly ValueComparer<RuleOutcome> OutcomeComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeOutcome(left)
                == RulePersistenceJson.SerializeOutcome(right),
            value => RulePersistenceJson.SerializeOutcome(value).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeOutcome(RulePersistenceJson.SerializeOutcome(value))!);

    public void Configure(EntityTypeBuilder<RuleDefinitionVersion> builder)
    {
        builder.ToTable("rule_definition_versions");
        builder.HasKey(version => version.Id);

        builder.Property(version => version.Id)
            .HasColumnName("id")
            .HasConversion(RuleValueConverters.DefinitionVersionId)
            .ValueGeneratedNever();
        builder.Property(version => version.DefinitionId)
            .HasColumnName("rule_definition_id")
            .HasConversion(RuleValueConverters.DefinitionId)
            .IsRequired();
        builder.Property(version => version.Version)
            .HasColumnName("version_number")
            .IsRequired();
        builder.Property(version => version.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(version => version.Description)
            .HasColumnName("description")
            .HasMaxLength(1000)
            .IsRequired();
        builder.Property(version => version.Scope)
            .HasColumnName("scope")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(version => version.ContextKey)
            .HasColumnName("context_key")
            .HasMaxLength(120)
            .HasConversion(RuleValueConverters.ContextKey)
            .IsRequired();
        builder.Property(version => version.ContextSchemaVersion)
            .HasColumnName("context_schema_version")
            .IsRequired();
        builder.Property(version => version.OutcomeKind)
            .HasColumnName("outcome_kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Ignore(version => version.Parameters);
        builder.Property<List<RuleParameterDefinition>>("_parameters")
            .HasColumnName("parameters")
            .HasColumnType("jsonb")
            .HasConversion(ParametersConverter)
            .IsRequired()
            .Metadata.SetValueComparer(ParametersComparer);
        builder.Property(version => version.Condition)
            .HasColumnName("condition")
            .HasColumnType("jsonb")
            .HasConversion(ConditionConverter)
            .IsRequired()
            .Metadata.SetValueComparer(ConditionComparer);
        builder.Property(version => version.Outcome)
            .HasColumnName("outcome")
            .HasColumnType("jsonb")
            .HasConversion(OutcomeConverter)
            .IsRequired()
            .Metadata.SetValueComparer(OutcomeComparer);
        builder.Property(version => version.PublishedByUserId)
            .HasColumnName("published_by_user_id")
            .IsRequired();
        builder.Property(version => version.PublishedAt)
            .HasColumnName("published_at")
            .IsRequired();

        builder.HasIndex(version => new { version.DefinitionId, version.Version })
            .IsUnique();
    }
}
