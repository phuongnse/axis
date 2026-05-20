using System.Text.Json;
using Axis.WorkflowEngine.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.WorkflowEngine.Infrastructure.Persistence.Configurations;

internal sealed class ExecutionConfiguration : IEntityTypeConfiguration<WorkflowExecution>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        builder.ToTable("workflow_executions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.WorkflowDefinitionId).IsRequired();
        builder.Property(e => e.OrganizationId).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().IsRequired();
        builder.Property(e => e.TriggerType).HasConversion<string>().IsRequired();
        builder.Property(e => e.TriggeredByUserId);
        builder.Property(e => e.RetryOfExecutionId);
        builder.Property(e => e.ErrorMessage);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.StartedAt);
        builder.Property(e => e.CompletedAt);
        builder.Property(e => e.DeletedAt);

        builder.HasQueryFilter(e => !e.DeletedAt.HasValue);

        ValueConverter<Dictionary<string, object?>, string> contextConverter = new(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, JsonOptions) ?? new Dictionary<string, object?>());

        ValueComparer<Dictionary<string, object?>> contextComparer = new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(v, JsonOptions), JsonOptions) ?? new Dictionary<string, object?>());

        builder.Property<Dictionary<string, object?>>("_context")
            .HasField("_context")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("context")
            .HasColumnType("jsonb")
            .HasConversion(contextConverter, contextComparer)
            .IsRequired();

        builder.Ignore(e => e.Context);

        ValueConverter<IReadOnlyDictionary<string, object?>?, string?> snapshotConverter = new(
            v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object?>>(v, JsonOptions));

        ValueComparer<IReadOnlyDictionary<string, object?>?> snapshotComparer = new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => v == null ? null : (IReadOnlyDictionary<string, object?>?)JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(v, JsonOptions), JsonOptions));

        builder.Navigation(e => e.Steps)
            .HasField("_steps")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(e => e.Steps, stepBuilder =>
        {
            stepBuilder.ToTable("execution_steps");

            stepBuilder.WithOwner().HasForeignKey(s => s.ExecutionId);

            stepBuilder.HasKey(s => s.Id);

            stepBuilder.Property(s => s.ExecutionId).IsRequired();
            stepBuilder.Property(s => s.OrganizationId).IsRequired();
            stepBuilder.Property(s => s.StepDefinitionId).IsRequired();
            stepBuilder.Property(s => s.Name).IsRequired().HasMaxLength(500);
            stepBuilder.Property(s => s.DisplayOrder).IsRequired();
            stepBuilder.Property(s => s.CreatedAt).IsRequired();
            stepBuilder.Property(s => s.StartedAt);
            stepBuilder.Property(s => s.CompletedAt);
            stepBuilder.Property(s => s.ErrorDetails);

            stepBuilder.Property(s => s.StepType)
                .HasConversion<string>()
                .IsRequired();

            stepBuilder.Property(s => s.Status)
                .HasConversion<string>()
                .IsRequired();

            stepBuilder.Property(s => s.InputSnapshot)
                .HasColumnType("jsonb")
                .HasConversion(snapshotConverter, snapshotComparer);

            stepBuilder.Property(s => s.OutputSnapshot)
                .HasColumnType("jsonb")
                .HasConversion(snapshotConverter, snapshotComparer);

            stepBuilder.HasIndex(s => new { s.ExecutionId, s.OrganizationId });

            // Optimistic concurrency via PostgreSQL xmin system column.
            // EF Core includes "WHERE xmin = @loadedXmin" in UPDATEs so a concurrent
            // write to the same step row raises DbUpdateConcurrencyException, translated
            // to ConcurrencyException by UnitOfWork. No migration needed — xmin is a
            // PostgreSQL built-in system column present on every row.
            stepBuilder.UseXminAsConcurrencyToken();
        });
    }
}
