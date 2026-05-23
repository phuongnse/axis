using System.Text.Json;
using axis.workflowbuilder.events;
using Axis.Shared.Application;
using Axis.WorkflowBuilder.Contracts;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EngineReadModels = Axis.WorkflowEngine.Domain.ReadModels;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

internal sealed class WorkflowPublishedHandler(
    WorkflowEngineDbContext context,
    IUnitOfWork uow,
    ILogger<WorkflowPublishedHandler> logger)
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task Handle(WorkflowPublishedEvent @event, CancellationToken ct)
    {
        await UpsertActiveStatusAsync(@event, ct);
        await UpsertSnapshotAsync(@event, ct);
    }

    private async Task UpsertActiveStatusAsync(WorkflowPublishedEvent @event, CancellationToken ct)
    {
        Guid workflowId = @event.WorkflowId();
        Guid organizationId = @event.OrganizationId();

        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId, ct);

        if (existing is null)
            context.WorkflowActiveStatuses.Add(
                WorkflowActiveStatus.Activated(workflowId, organizationId));
        else
            existing.Reactivate();

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent delivery detected for active status of workflow {WorkflowId} — skipping",
                workflowId);
            return;
        }
        catch (UniqueConstraintException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent insert detected for active status of workflow {WorkflowId} — skipping",
                workflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowPublishedHandler: active status upserted for workflow {WorkflowId} org {OrganizationId}",
            workflowId, organizationId);
    }

    private async Task UpsertSnapshotAsync(WorkflowPublishedEvent @event, CancellationToken ct)
    {
        Guid workflowId = @event.WorkflowId();
        Guid organizationId = @event.OrganizationId();
        IReadOnlyList<EngineReadModels.StepDefinitionSnapshot> steps = MapSteps(@event.steps);
        IReadOnlyList<EngineReadModels.TransitionSnapshot> transitions = MapTransitions(@event.transitions);

        EngineReadModels.WorkflowSnapshot? existing = await context.WorkflowSnapshots
            .FirstOrDefaultAsync(w => w.WorkflowId == workflowId, ct);

        if (existing is null)
            context.WorkflowSnapshots.Add(
                EngineReadModels.WorkflowSnapshot.Create(workflowId, organizationId, steps, transitions));
        else
            existing.Update(steps, transitions);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (ConcurrencyException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent delivery detected for snapshot of workflow {WorkflowId} — skipping",
                workflowId);
            return;
        }
        catch (UniqueConstraintException)
        {
            logger.LogWarning(
                "WorkflowPublishedHandler: concurrent insert detected for snapshot of workflow {WorkflowId} — skipping",
                workflowId);
            return;
        }

        logger.LogInformation(
            "WorkflowPublishedHandler: snapshot upserted for workflow {WorkflowId} with {StepCount} steps",
            workflowId, steps.Count);
    }

    private static IReadOnlyList<EngineReadModels.StepDefinitionSnapshot> MapSteps(
        IList<StepSnapshotRecord> eventSteps)
        => eventSteps.Select(s => new EngineReadModels.StepDefinitionSnapshot
        {
            Id = Guid.Parse(s.id),
            Name = s.name,
            StepType = MapStepType(s.stepType),
            DisplayOrder = s.displayOrder,
            Config = DeserializeConfig(s.configJson),
        }).ToList();

    private static IReadOnlyList<EngineReadModels.TransitionSnapshot> MapTransitions(
        IList<TransitionSnapshotRecord> eventTransitions)
        => eventTransitions.Select(t => new EngineReadModels.TransitionSnapshot
        {
            FromStepId = Guid.Parse(t.fromStepId),
            ToStepId = Guid.Parse(t.toStepId),
            Label = t.label,
        }).ToList();

    private static Dictionary<string, object?>? DeserializeConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(configJson, ConfigJsonOptions);
    }

    private static StepType MapStepType(string stepTypeStr)
        => Enum.TryParse<StepType>(stepTypeStr, ignoreCase: true, out StepType result)
            ? result
            : throw new InvalidOperationException($"Unknown step type: {stepTypeStr}");
}
