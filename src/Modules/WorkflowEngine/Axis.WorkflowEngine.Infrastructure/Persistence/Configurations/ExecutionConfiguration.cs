using System.Text.Json;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
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

        builder.Property(e => e.WorkflowDefinitionId)
            .IsRequired();

        builder.Property(e => e.OrganizationId)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.TriggerType)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.TriggeredByUserId);
        builder.Property(e => e.RetryOfExecutionId);
        builder.Property(e => e.ErrorMessage);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.StartedAt);
        builder.Property(e => e.CompletedAt);

        var contextConverter = new ValueConverter<Dictionary<string, object?>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, JsonOptions) ?? new Dictionary<string, object?>());

        builder.Property<Dictionary<string, object?>>("_context")
            .HasField("_context")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("context")
            .HasColumnType("jsonb")
            .HasConversion(contextConverter)
            .IsRequired();

        builder.Ignore(e => e.Context);
    }
}
