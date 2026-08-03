using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.BusinessObjects.Infrastructure.Persistence.Configurations;

internal sealed class BusinessObjectRecordConfiguration : IEntityTypeConfiguration<BusinessObjectRecord>
{
    private static readonly ValueConverter<Dictionary<string, IReadOnlyList<string>>, string> ValuesConverter =
        new(
            value => BusinessObjectRecordPersistenceJson.SerializeValues(value),
            value => BusinessObjectRecordPersistenceJson.DeserializeValues(value));

    private static readonly ValueComparer<Dictionary<string, IReadOnlyList<string>>> ValuesComparer =
        new(
            (left, right) => BusinessObjectRecordPersistenceJson.SerializeValues(left ?? new()) ==
                BusinessObjectRecordPersistenceJson.SerializeValues(right ?? new()),
            value => BusinessObjectRecordPersistenceJson.SerializeValues(value ?? new()).GetHashCode(StringComparison.Ordinal),
            value => BusinessObjectRecordPersistenceJson.DeserializeValues(
                BusinessObjectRecordPersistenceJson.SerializeValues(value ?? new())));

    private static readonly ValueConverter<List<BusinessObjectRecordRuleEvaluation>, string> EvaluationsConverter =
        new(
            value => BusinessObjectRecordPersistenceJson.SerializeRuleEvaluations(value),
            value => BusinessObjectRecordPersistenceJson.DeserializeRuleEvaluations(value));

    private static readonly ValueComparer<List<BusinessObjectRecordRuleEvaluation>> EvaluationsComparer =
        new(
            (left, right) => BusinessObjectRecordPersistenceJson.SerializeRuleEvaluations(left ?? new List<BusinessObjectRecordRuleEvaluation>()) ==
                BusinessObjectRecordPersistenceJson.SerializeRuleEvaluations(right ?? new List<BusinessObjectRecordRuleEvaluation>()),
            value => BusinessObjectRecordPersistenceJson.SerializeRuleEvaluations(value ?? new List<BusinessObjectRecordRuleEvaluation>()).GetHashCode(StringComparison.Ordinal),
            value => BusinessObjectRecordPersistenceJson.DeserializeRuleEvaluations(
                BusinessObjectRecordPersistenceJson.SerializeRuleEvaluations(value ?? new List<BusinessObjectRecordRuleEvaluation>())));

    public void Configure(EntityTypeBuilder<BusinessObjectRecord> builder)
    {
        builder.ToTable("business_object_records");
        builder.HasKey(record => record.Id);

        builder.Property(record => record.Id)
            .HasColumnName("id")
            .HasConversion(BusinessObjectValueConverters.RecordId)
            .ValueGeneratedNever();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(record => record.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        builder.Property(record => record.DefinitionVersionId)
            .HasColumnName("definition_version_id")
            .HasConversion(BusinessObjectValueConverters.DefinitionVersionId)
            .IsRequired();
        builder.Property(record => record.DefinitionVersionNumber)
            .HasColumnName("definition_version_number")
            .IsRequired();
        builder.Property(record => record.ObjectKey)
            .HasColumnName("object_key")
            .HasMaxLength(63)
            .HasConversion(BusinessObjectValueConverters.DefinitionKey)
            .IsRequired();
        builder.Property(record => record.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(120)
            .IsRequired();
        builder.Property(record => record.PayloadHash)
            .HasColumnName("payload_hash")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(record => record.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(record => record.Revision).HasColumnName("revision").IsRequired();
        builder.Property(record => record.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(record => record.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(record => record.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        builder.Property(record => record.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(record => record.SubmittedAt).HasColumnName("submitted_at");

        builder.Ignore(record => record.Values);
        builder.Ignore(record => record.RuleEvaluations);
        builder.Property<Dictionary<string, IReadOnlyList<string>>>("_values")
            .HasColumnName("values")
            .HasColumnType("jsonb")
            .HasConversion(ValuesConverter)
            .IsRequired()
            .Metadata.SetValueComparer(ValuesComparer);

        builder.Property<List<BusinessObjectRecordRuleEvaluation>>("_ruleEvaluations")
            .HasColumnName("rule_evaluations")
            .HasColumnType("jsonb")
            .HasConversion(EvaluationsConverter)
            .IsRequired()
            .Metadata.SetValueComparer(EvaluationsComparer);

        builder.HasIndex(record => new { record.WorkspaceId, record.ObjectKey, record.IdempotencyKey })
            .IsUnique();
        builder.HasIndex(record => new { record.WorkspaceId, record.ObjectKey, record.UpdatedAt });
        builder.HasIndex(record => new { record.WorkspaceId, record.DefinitionVersionId });
    }
}
