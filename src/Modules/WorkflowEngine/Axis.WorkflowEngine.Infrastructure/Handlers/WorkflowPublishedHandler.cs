using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EngineReadModels = Axis.WorkflowEngine.Domain.ReadModels;
using BuilderEvents = Axis.WorkflowBuilder.Domain.Events;

namespace Axis.WorkflowEngine.Infrastructure.Handlers;

internal sealed class WorkflowPublishedHandler(WorkflowEngineDbContext context)
{
    public async Task Handle(WorkflowPublished @event, CancellationToken ct)
    {
        await UpsertActiveStatusAsync(@event, ct);
        await UpsertSnapshotAsync(@event, ct);
    }

    private async Task UpsertActiveStatusAsync(WorkflowPublished @event, CancellationToken ct)
    {
        WorkflowActiveStatus? existing = await context.WorkflowActiveStatuses
            .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

        if (existing is null)
            context.WorkflowActiveStatuses.Add(
                WorkflowActiveStatus.Activated(@event.WorkflowId, @event.OrganizationId));
        else
            existing.Reactivate();

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent duplicate event delivery — row already inserted by a parallel handler invocation.
        }
    }

    private async Task UpsertSnapshotAsync(WorkflowPublished @event, CancellationToken ct)
    {
        IReadOnlyList<EngineReadModels.StepDefinitionSnapshot> steps = MapSteps(@event.Steps);
        IReadOnlyList<EngineReadModels.TransitionSnapshot> transitions = MapTransitions(@event.Transitions);

        EngineReadModels.WorkflowSnapshot? existing = await context.WorkflowSnapshots
            .FirstOrDefaultAsync(w => w.WorkflowId == @event.WorkflowId, ct);

        if (existing is null)
            context.WorkflowSnapshots.Add(
                EngineReadModels.WorkflowSnapshot.Create(@event.WorkflowId, @event.OrganizationId, steps, transitions));
        else
            existing.Update(steps, transitions);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent duplicate event delivery — row already upserted by parallel handler.
        }
    }

    private static IReadOnlyList<EngineReadModels.StepDefinitionSnapshot> MapSteps(
        IReadOnlyList<BuilderEvents.StepSnapshot> eventSteps)
        => eventSteps.Select(s => new EngineReadModels.StepDefinitionSnapshot
        {
            Id = s.Id,
            Name = s.Name,
            StepType = MapStepType(s.StepType),
            DisplayOrder = s.DisplayOrder,
            Config = s.Config
        }).ToList();

    private static IReadOnlyList<EngineReadModels.TransitionSnapshot> MapTransitions(
        IReadOnlyList<BuilderEvents.TransitionSnapshot> eventTransitions)
        => eventTransitions.Select(t => new EngineReadModels.TransitionSnapshot
        {
            FromStepId = t.FromStepId,
            ToStepId = t.ToStepId,
            Label = t.Label
        }).ToList();

    private static StepType MapStepType(string stepTypeStr)
        => Enum.TryParse<StepType>(stepTypeStr, ignoreCase: true, out StepType result)
            ? result
            : throw new InvalidOperationException($"Unknown step type: {stepTypeStr}");
}
