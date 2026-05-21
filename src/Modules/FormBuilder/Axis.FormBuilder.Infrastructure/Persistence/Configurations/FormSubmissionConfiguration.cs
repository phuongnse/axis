using System.Text.Json;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.FormBuilder.Infrastructure.Persistence.Configurations;

internal sealed class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    private static readonly ValueConverter<IReadOnlyDictionary<string, object?>, string> DataConverter = new(
        data => JsonSerializer.Serialize(data, FormJsonOptions.Options),
        json => JsonSerializer.Deserialize<Dictionary<string, object?>>(json, FormJsonOptions.Options)
            ?? new Dictionary<string, object?>());

    private static readonly ValueComparer<IReadOnlyDictionary<string, object?>> DataComparer = new(
        (l1, l2) => JsonSerializer.Serialize(l1, FormJsonOptions.Options)
                 == JsonSerializer.Serialize(l2, FormJsonOptions.Options),
        l => JsonSerializer.Serialize(l, FormJsonOptions.Options).GetHashCode(),
        l => JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(l, FormJsonOptions.Options),
            FormJsonOptions.Options) ?? new Dictionary<string, object?>());

    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable("form_submissions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.FormDefinitionId).HasColumnName("form_definition_id").IsRequired();
        builder.Property(s => s.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(s => s.ExecutionId).HasColumnName("execution_id").IsRequired();
        builder.Property(s => s.ExecutionStepId).HasColumnName("execution_step_id").IsRequired();
        builder.Property(s => s.AssigneeUserId).HasColumnName("assignee_user_id");
        builder.Property(s => s.AssigneeRoleId).HasColumnName("assignee_role_id");
        builder.Property(s => s.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        builder.Property(s => s.AccessToken).HasColumnName("access_token").IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>().IsRequired();
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at");
        builder.Property(s => s.SubmittedAt).HasColumnName("submitted_at");

        builder.Property(s => s.SubmittedData)
            .HasColumnName("submitted_data")
            .HasColumnType("jsonb")
            .HasConversion(DataConverter)
            .Metadata.SetValueComparer(DataComparer);

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(200).IsRequired();

        builder.HasIndex(s => s.AccessToken).IsUnique();
        builder.HasIndex(s => new { s.ExecutionId, s.ExecutionStepId }).IsUnique();
        builder.HasIndex(s => new { s.AssigneeUserId, s.Status });
    }
}
