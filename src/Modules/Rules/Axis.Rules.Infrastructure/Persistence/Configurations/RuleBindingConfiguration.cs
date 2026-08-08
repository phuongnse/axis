using Axis.Rules.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.Rules.Infrastructure.Persistence.Configurations;

internal sealed class RuleBindingConfiguration : IEntityTypeConfiguration<RuleBinding>
{
    private static readonly ValueConverter<Dictionary<string, RuleInputMapping>, string> MappingsConverter =
        new(
            value => RulePersistenceJson.SerializeInputMappings(value),
            value => RulePersistenceJson.DeserializeInputMappings(value));

    private static readonly ValueComparer<Dictionary<string, RuleInputMapping>> MappingsComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeInputMappings(left ?? new()) == RulePersistenceJson.SerializeInputMappings(right ?? new()),
            value => RulePersistenceJson.SerializeInputMappings(value ?? new()).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeInputMappings(RulePersistenceJson.SerializeInputMappings(value ?? new())));

    private static readonly ValueConverter<List<RuleBindingRevision>, string> RevisionHistoryConverter =
        new(
            value => RulePersistenceJson.SerializeBindingRevisionHistory(value),
            value => RulePersistenceJson.DeserializeBindingRevisionHistory(value));

    private static readonly ValueComparer<List<RuleBindingRevision>> RevisionHistoryComparer =
        new(
            (left, right) => RulePersistenceJson.SerializeBindingRevisionHistory(left ?? new List<RuleBindingRevision>()) ==
                RulePersistenceJson.SerializeBindingRevisionHistory(right ?? new List<RuleBindingRevision>()),
            value => RulePersistenceJson.SerializeBindingRevisionHistory(value ?? new List<RuleBindingRevision>()).GetHashCode(StringComparison.Ordinal),
            value => RulePersistenceJson.DeserializeBindingRevisionHistory(
                RulePersistenceJson.SerializeBindingRevisionHistory(value ?? new List<RuleBindingRevision>())));

    public void Configure(EntityTypeBuilder<RuleBinding> builder)
    {
        builder.ToTable("rule_bindings", table => table.HasCheckConstraint(
            "CK_rule_bindings_installed_provenance",
            """
            (installed_solution_version_id IS NULL AND installed_component_key IS NULL AND
             installed_component_hash IS NULL AND installed_operation_id IS NULL AND
             installed_step_id IS NULL AND installed_lease_epoch IS NULL)
            OR
            (installed_solution_version_id IS NOT NULL AND installed_component_key IS NOT NULL AND
             installed_component_hash IS NOT NULL AND installed_operation_id IS NOT NULL AND
             installed_step_id IS NOT NULL AND installed_lease_epoch > 0 AND
             installed_component_key ~ '^[a-z][a-z0-9_.:@-]{0,199}$' AND
             installed_component_hash ~ '^[0-9a-f]{64}$' AND
             installed_solution_version_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
             installed_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND
             installed_step_id <> '00000000-0000-0000-0000-000000000000'::uuid)
            """));
        builder.HasKey(binding => binding.Id);
        builder.Property(binding => binding.Id)
            .HasColumnName("id")
            .HasConversion(RuleValueConverters.BindingId)
            .ValueGeneratedNever();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(binding => binding.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(binding => binding.DefinitionKey)
            .HasColumnName("definition_key")
            .HasMaxLength(120)
            .HasConversion(RuleValueConverters.DefinitionKey)
            .IsRequired();
        builder.Property(binding => binding.DefinitionVersion).HasColumnName("definition_version").IsRequired();
        builder.Property(binding => binding.TargetType).HasColumnName("target_type").HasMaxLength(120).IsRequired();
        builder.Property(binding => binding.TargetId).HasColumnName("target_id").HasMaxLength(200).IsRequired();
        builder.Property(binding => binding.UseCaseOrTrigger).HasColumnName("use_case_or_trigger").HasMaxLength(120).IsRequired();
        builder.Ignore(binding => binding.InputMappings);
        builder.Ignore(binding => binding.RevisionHistory);
        builder.Property<Dictionary<string, RuleInputMapping>>("_inputMappings")
            .HasColumnName("input_mappings")
            .HasColumnType("jsonb")
            .HasConversion(MappingsConverter)
            .IsRequired()
            .Metadata.SetValueComparer(MappingsComparer);
        builder.Property(binding => binding.Priority).HasColumnName("priority").IsRequired();
        builder.Property(binding => binding.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(binding => binding.FailureBehavior).HasColumnName("failure_behavior").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(binding => binding.Revision).HasColumnName("revision").IsRequired();
        builder.Property<List<RuleBindingRevision>>("_revisionHistory")
            .HasColumnName("revision_history")
            .HasColumnType("jsonb")
            .HasConversion(RevisionHistoryConverter)
            .IsRequired()
            .Metadata.SetValueComparer(RevisionHistoryComparer);
        Subject(builder.ComplexProperty(binding => binding.CreatedBySubject), "created_by_subject");
        Subject(builder.ComplexProperty(binding => binding.UpdatedBySubject), "updated_by_subject");
        builder.Property(binding => binding.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(binding => binding.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(binding => binding.InstalledSolutionVersionId).HasColumnName("installed_solution_version_id");
        builder.Property(binding => binding.InstalledComponentKey).HasColumnName("installed_component_key").HasMaxLength(200);
        builder.Property(binding => binding.InstalledComponentHash).HasColumnName("installed_component_hash").HasMaxLength(64);
        builder.Property(binding => binding.InstalledOperationId).HasColumnName("installed_operation_id");
        builder.Property(binding => binding.InstalledStepId).HasColumnName("installed_step_id");
        builder.Property(binding => binding.InstalledLeaseEpoch).HasColumnName("installed_lease_epoch");
        builder.Ignore(binding => binding.IsInstalled);
        builder.HasIndex(binding => new { binding.WorkspaceId, binding.TargetType, binding.TargetId, binding.UseCaseOrTrigger, binding.DefinitionKey, binding.DefinitionVersion });
        builder.HasIndex(binding => new { binding.WorkspaceId, binding.DefinitionKey, binding.DefinitionVersion });
        builder.HasIndex(binding => new { binding.WorkspaceId, binding.InstalledComponentKey })
            .IsUnique()
            .HasFilter("installed_component_key IS NOT NULL");
    }

    private static void Subject(ComplexPropertyBuilder<RuleSubjectReference> subject, string prefix)
    {
        subject.Property(value => value.Kind).HasColumnName(prefix + "_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        subject.Property(value => value.Id).HasColumnName(prefix + "_id").IsRequired();
    }
}
