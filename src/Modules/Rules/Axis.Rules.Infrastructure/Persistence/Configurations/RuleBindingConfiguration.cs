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

    public void Configure(EntityTypeBuilder<RuleBinding> builder)
    {
        builder.ToTable("rule_bindings");
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
        builder.Property(binding => binding.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(binding => binding.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(binding => binding.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(binding => binding.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(binding => new { binding.WorkspaceId, binding.TargetType, binding.TargetId, binding.UseCaseOrTrigger, binding.DefinitionKey, binding.DefinitionVersion });
        builder.HasIndex(binding => new { binding.WorkspaceId, binding.DefinitionKey, binding.DefinitionVersion });
    }
}
