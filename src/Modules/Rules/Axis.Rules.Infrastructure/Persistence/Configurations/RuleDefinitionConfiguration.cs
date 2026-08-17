using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;

namespace Axis.Rules.Infrastructure.Persistence.Configurations;

internal sealed class RuleDefinitionConfiguration : IEntityTypeConfiguration<RuleDefinition>
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

    private static readonly ValueConverter<RuleConditionNode?, string> ConditionConverter =
        new(
            value => RulePersistenceJson.SerializeCondition(value),
            value => RulePersistenceJson.DeserializeCondition(value));

    private static readonly ValueComparer<RuleConditionNode?> ConditionComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeCondition(left) == RulePersistenceJson.SerializeCondition(right),
            value => RulePersistenceJson.SerializeCondition(value).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeCondition(RulePersistenceJson.SerializeCondition(value)));

    private static readonly ValueConverter<RuleOutputContract, string> OutputConverter =
        new(
            value => RulePersistenceJson.SerializeOutput(value),
            value => RulePersistenceJson.DeserializeOutput(value));

    private static readonly ValueComparer<RuleOutputContract> OutputComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeOutput(left!) == RulePersistenceJson.SerializeOutput(right!),
            value => RulePersistenceJson.SerializeOutput(value!).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeOutput(RulePersistenceJson.SerializeOutput(value!)));

    public void Configure(EntityTypeBuilder<RuleDefinition> builder)
    {
        builder.ToTable("rule_definitions");
        builder.HasKey(definition => definition.Id);

        builder.Property(definition => definition.Id)
            .HasColumnName("id")
            .HasConversion(RuleValueConverters.DefinitionId)
            .ValueGeneratedNever();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(definition => definition.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(definition => definition.Key)
            .HasColumnName("definition_key")
            .HasMaxLength(63)
            .HasConversion(RuleValueConverters.DefinitionKey)
            .IsRequired();
        builder.Property(definition => definition.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(definition => definition.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
        builder.Property(definition => definition.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(definition => definition.ExpressionLanguageVersion).HasColumnName("expression_language_version").IsRequired();
        builder.Ignore(definition => definition.Status);
        builder.Property(definition => definition.Revision).HasColumnName("revision").IsRequired();
        builder.Property(definition => definition.LatestPublishedVersion).HasColumnName("latest_published_version");
        builder.Property(definition => definition.ActiveVersion).HasColumnName("active_version");
        builder.Ignore(definition => definition.Inputs);
        builder.Ignore(definition => definition.Documentation);
        builder.Property<List<RuleInputDefinition>>("_inputs")
            .HasColumnName("inputs")
            .HasColumnType("jsonb")
            .HasConversion(InputsConverter)
            .IsRequired()
            .Metadata.SetValueComparer(InputsComparer);
        builder.Property(definition => definition.Condition)
            .HasColumnName("condition")
            .HasColumnType("jsonb")
            .HasConversion(ConditionConverter)
            .Metadata.SetValueComparer(ConditionComparer);
        builder.Property(definition => definition.Output)
            .HasColumnName("output")
            .HasColumnType("jsonb")
            .HasConversion(OutputConverter)
            .IsRequired()
            .Metadata.SetValueComparer(OutputComparer);
        Subject(builder.ComplexProperty(definition => definition.CreatedBySubject), "created_by_subject");
        Subject(builder.ComplexProperty(definition => definition.UpdatedBySubject), "updated_by_subject");
        builder.Property(definition => definition.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(definition => definition.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property<ActorKind?>("CreatedByActorKind").HasColumnName("created_by_actor_kind").HasConversion<string>().HasMaxLength(32);
        builder.Property<Guid?>("CreatedByActorSubjectId").HasColumnName("created_by_actor_subject_id");
        builder.Property<string?>("CreatedByActorDisplayName").HasColumnName("created_by_actor_display_name").HasMaxLength(ActorSnapshot.MaximumDisplayNameLength);
        builder.Property<ActorKind?>("UpdatedByActorKind").HasColumnName("updated_by_actor_kind").HasConversion<string>().HasMaxLength(32);
        builder.Property<Guid?>("UpdatedByActorSubjectId").HasColumnName("updated_by_actor_subject_id");
        builder.Property<string?>("UpdatedByActorDisplayName").HasColumnName("updated_by_actor_display_name").HasMaxLength(ActorSnapshot.MaximumDisplayNameLength);
        builder.Ignore(definition => definition.CreatedByActor);
        builder.Ignore(definition => definition.UpdatedByActor);
        builder.Ignore(definition => definition.ArchivedBySubject);
        builder.Property<RuleSubjectKind?>("ArchivedBySubjectKind")
            .HasColumnName("archived_by_subject_kind")
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property<Guid?>("ArchivedBySubjectId")
            .HasColumnName("archived_by_subject_id");
        builder.Property(definition => definition.ArchivedAt).HasColumnName("archived_at");

        builder.Property<string>("SearchTitle")
            .HasColumnName("search_title")
            .HasComputedColumnSql("axis_unaccent(lower(coalesce(name, '')))", stored: true);
        builder.Property<string>("SearchText")
            .HasColumnName("search_text")
            .HasComputedColumnSql("axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(description, '') || ' ' || coalesce(definition_key, '')))", stored: true);
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql("to_tsvector('simple', axis_unaccent(lower(coalesce(name, '') || ' ' || coalesce(description, '') || ' ' || coalesce(definition_key, ''))))", stored: true);

        builder.HasIndex(definition => new { definition.WorkspaceId, definition.Key }).IsUnique();
        builder.HasIndex(definition => new { definition.WorkspaceId, definition.ArchivedAt, definition.ActiveVersion, definition.LatestPublishedVersion, definition.Name });
        builder.HasIndex("SearchTitle").HasDatabaseName("ix_rule_definitions_search_title").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex("SearchText").HasDatabaseName("ix_rule_definitions_search_text").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex("SearchVector").HasDatabaseName("ix_rule_definitions_search_vector").HasMethod("gin");

        builder.HasMany(definition => definition.Versions)
            .WithOne()
            .HasForeignKey(version => version.DefinitionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.Navigation(definition => definition.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void Subject(ComplexPropertyBuilder<RuleSubjectReference> subject, string prefix)
    {
        subject.Property(value => value.Kind).HasColumnName(prefix + "_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        subject.Property(value => value.Id).HasColumnName(prefix + "_id").IsRequired();
    }

}
