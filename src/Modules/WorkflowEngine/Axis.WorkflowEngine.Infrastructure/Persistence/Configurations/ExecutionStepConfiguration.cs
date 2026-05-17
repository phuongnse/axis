using System.Text.Json;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.WorkflowEngine.Infrastructure.Persistence.Configurations;

internal sealed class ExecutionStepConfiguration : IEntityTypeConfiguration<ExecutionStep>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<ExecutionStep> builder)
    {
        builder.ToTable("execution_steps");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ExecutionId).IsRequired();
        builder.Property(s => s.OrganizationId).IsRequired();
        builder.Property(s => s.StepDefinitionId).IsRequired();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(500);
        builder.Property(s => s.DisplayOrder).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.StartedAt);
        builder.Property(s => s.CompletedAt);
        builder.Property(s => s.ErrorDetails);

        builder.Property(s => s.StepType)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .IsRequired();

        ValueConverter<IReadOnlyDictionary<string, object?>?, string?> snapshotConverter = new(
            v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(v, JsonOptions));

        ValueComparer<IReadOnlyDictionary<string, object?>?> snapshotComparer = new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => v == null ? null : (IReadOnlyDictionary<string, object?>?)JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(v, JsonOptions), JsonOptions));

        builder.Property(s => s.InputSnapshot)
            .HasColumnType("jsonb")
            .HasConversion(snapshotConverter, snapshotComparer);

        builder.Property(s => s.OutputSnapshot)
            .HasColumnType("jsonb")
            .HasConversion(snapshotConverter, snapshotComparer);

        builder.HasIndex(s => new { s.ExecutionId, s.OrganizationId });
    }
}
