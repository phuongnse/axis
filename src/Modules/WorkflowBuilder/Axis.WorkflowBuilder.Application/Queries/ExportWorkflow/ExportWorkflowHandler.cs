using Axis.Shared.Application.CQRS;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;

namespace Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;

public sealed class ExportWorkflowHandler(IWorkflowRepository workflowRepo)
    : IQueryHandler<ExportWorkflowQuery, WorkflowExportDto?>
{
    private static readonly string[] SensitivePatterns =
    [
        "token", "api_key", "apikey", "secret", "password", "authorization",
        "auth_token", "hmac_secret", "client_secret", "private_key", "bearer",
        "access_token", "refresh_token"
    ];

    public async Task<WorkflowExportDto?> Handle(ExportWorkflowQuery query, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            query.WorkflowId, query.workspaceId, cancellationToken);

        if (workflow is null)
            return null;

        return ToExportDto(workflow);
    }

    internal static WorkflowExportDto ToExportDto(WorkflowDefinition workflow) =>
        new(
            workflow.Name,
            workflow.Description,
            workflow.Steps.Select(s => new StepExportDto(s.Id, s.Name, s.Type, ScrubConfig(s.Config))).ToList(),
            workflow.Transitions.Select(t => new TransitionExportDto(t.FromStepId, t.ToStepId, t.Label)).ToList(),
            workflow.Triggers.Select(t => new TriggerExportDto(t.Type, ScrubConfig(t.Config))).ToList());

    private static bool IsSensitiveKey(string key) =>
        SensitivePatterns.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static object? ScrubValue(string key, object? value)
    {
        if (IsSensitiveKey(key))
            return "[REDACTED]";

        return value switch
        {
            IReadOnlyDictionary<string, object?> nested => ScrubConfig(nested),
            IDictionary<string, object?> nested => ScrubConfig(
                new Dictionary<string, object?>(nested)),
            IEnumerable<object?> list => list.Select(item =>
                item is IReadOnlyDictionary<string, object?> d ? ScrubConfig(d) : item).ToList(),
            _ => value
        };
    }

    private static IReadOnlyDictionary<string, object?>? ScrubConfig(IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null)
            return null;

        return config.ToDictionary(kvp => kvp.Key, kvp => ScrubValue(kvp.Key, kvp.Value));
    }
}
