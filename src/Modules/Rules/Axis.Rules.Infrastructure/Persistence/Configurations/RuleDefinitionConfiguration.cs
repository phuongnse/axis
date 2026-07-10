using Axis.Rules.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Rules.Infrastructure.Persistence.Configurations;

internal sealed class RuleDefinitionConfiguration : IEntityTypeConfiguration<RuleDefinition>
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

    private static readonly ValueConverter<RuleConditionNode?, string> ConditionConverter =
        new(
            value => RulePersistenceJson.SerializeCondition(value),
            value => RulePersistenceJson.DeserializeCondition(value));

    private static readonly ValueComparer<RuleConditionNode?> ConditionComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeCondition(left)
                == RulePersistenceJson.SerializeCondition(right),
            value => RulePersistenceJson.SerializeCondition(value).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeCondition(RulePersistenceJson.SerializeCondition(value)));

    private static readonly ValueConverter<RuleOutcome?, string> OutcomeConverter =
        new(
            value => RulePersistenceJson.SerializeOutcome(value),
            value => RulePersistenceJson.DeserializeOutcome(value));

    private static readonly ValueComparer<RuleOutcome?> OutcomeComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeOutcome(left)
                == RulePersistenceJson.SerializeOutcome(right),
            value => RulePersistenceJson.SerializeOutcome(value).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeOutcome(RulePersistenceJson.SerializeOutcome(value)));

    public void Configure(EntityTypeBuilder<RuleDefinition> builder)
    {
        builder.ToTable("rule_definitions");
        builder.HasKey(definition => definition.Id);

        builder.Property(definition => definition.Id)
            .HasColumnName("id")
            .HasConversion(RuleValueConverters.DefinitionId)
            .ValueGeneratedNever();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(definition => definition.WorkspaceId)
            .HasColumnName("workspace_id")
            .IsRequired();
        builder.Property(definition => definition.Key)
            .HasColumnName("definition_key")
            .HasMaxLength(63)
            .HasConversion(RuleValueConverters.DefinitionKey)
            .IsRequired();
        builder.Property(definition => definition.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(definition => definition.Description)
            .HasColumnName("description")
            .HasMaxLength(1000)
            .IsRequired();
        builder.Property(definition => definition.Scope)
            .HasColumnName("scope")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(definition => definition.ContextKey)
            .HasColumnName("context_key")
            .HasMaxLength(120)
            .HasConversion(RuleValueConverters.ContextKey)
            .IsRequired();
        builder.Property(definition => definition.ContextSchemaVersion)
            .HasColumnName("context_schema_version")
            .IsRequired();
        builder.Property(definition => definition.OutcomeKind)
            .HasColumnName("outcome_kind")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(definition => definition.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(definition => definition.Revision)
            .HasColumnName("revision")
            .IsRequired();
        builder.Property(definition => definition.LatestPublishedVersion)
            .HasColumnName("latest_published_version");
        builder.Ignore(definition => definition.Parameters);
        builder.Property<List<RuleParameterDefinition>>("_parameters")
            .HasColumnName("parameters")
            .HasColumnType("jsonb")
            .HasConversion(ParametersConverter)
            .IsRequired()
            .Metadata.SetValueComparer(ParametersComparer);
        builder.Property(definition => definition.Condition)
            .HasColumnName("condition")
            .HasColumnType("jsonb")
            .HasConversion(ConditionConverter)
            .Metadata.SetValueComparer(ConditionComparer);
        builder.Property(definition => definition.Outcome)
            .HasColumnName("outcome")
            .HasColumnType("jsonb")
            .HasConversion(OutcomeConverter)
            .Metadata.SetValueComparer(OutcomeComparer);
        builder.Property(definition => definition.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();
        builder.Property(definition => definition.UpdatedByUserId)
            .HasColumnName("updated_by_user_id")
            .IsRequired();
        builder.Property(definition => definition.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(definition => definition.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        builder.Property(definition => definition.ArchivedByUserId)
            .HasColumnName("archived_by_user_id");
        builder.Property(definition => definition.ArchivedAt)
            .HasColumnName("archived_at");

        builder.HasIndex(definition => new { definition.WorkspaceId, definition.Key })
            .IsUnique();
        builder.HasIndex(definition => new { definition.WorkspaceId, definition.Status, definition.Name });

        builder.HasMany(definition => definition.Versions)
            .WithOne()
            .HasForeignKey(version => version.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
        builder.Navigation(definition => definition.Versions)
            .HasField("_versions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
