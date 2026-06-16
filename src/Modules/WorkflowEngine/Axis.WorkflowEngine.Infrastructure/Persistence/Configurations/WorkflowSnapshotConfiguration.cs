using System.Text.Json;
using Axis.WorkflowEngine.Domain.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Axis.WorkflowEngine.Infrastructure.Persistence.Configurations;

internal sealed class WorkflowSnapshotConfiguration : IEntityTypeConfiguration<WorkflowSnapshot>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public void Configure(EntityTypeBuilder<WorkflowSnapshot> builder)
    {
        builder.ToTable("workflow_snapshots");
        builder.HasKey(w => w.WorkflowId);
        builder.Property(w => w.WorkflowId).HasColumnName("workflow_id");
        builder.Property(w => w.workspaceId).HasColumnName("workspace_id").IsRequired();

        // Steps stored as JSONB
        ValueConverter<IReadOnlyList<StepDefinitionSnapshot>, string> stepsConverter = new(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<StepDefinitionSnapshot>>(v, JsonOptions) as IReadOnlyList<StepDefinitionSnapshot>
                 ?? new List<StepDefinitionSnapshot>());

        ValueComparer<IReadOnlyList<StepDefinitionSnapshot>> stepsComparer = new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<List<StepDefinitionSnapshot>>(
                JsonSerializer.Serialize(v, JsonOptions), JsonOptions) as IReadOnlyList<StepDefinitionSnapshot>
                 ?? new List<StepDefinitionSnapshot>());

        builder.Property(w => w.Steps)
            .HasColumnName("steps")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(stepsConverter, stepsComparer);

        // Transitions stored as JSONB
        ValueConverter<IReadOnlyList<TransitionSnapshot>, string> transitionsConverter = new(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<List<TransitionSnapshot>>(v, JsonOptions) as IReadOnlyList<TransitionSnapshot>
                 ?? new List<TransitionSnapshot>());

        ValueComparer<IReadOnlyList<TransitionSnapshot>> transitionsComparer = new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<List<TransitionSnapshot>>(
                JsonSerializer.Serialize(v, JsonOptions), JsonOptions) as IReadOnlyList<TransitionSnapshot>
                 ?? new List<TransitionSnapshot>());

        builder.Property(w => w.Transitions)
            .HasColumnName("transitions")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(transitionsConverter, transitionsComparer);
    }
}
