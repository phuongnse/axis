using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Application.Commands.ImportWorkflow;

public sealed class ImportWorkflowHandler(
    IPlanLimitService planLimitService,
    IWorkflowRepository workflowRepo,
    IWorkflowReferenceSync referenceSync,
    IUnitOfWork uow)
    : ICommandHandler<ImportWorkflowCommand, Guid>
{
    public async Task<Result<Guid>> Handle(ImportWorkflowCommand command, CancellationToken cancellationToken)
    {
        Result planCheck = await planLimitService.EnsureWithinLimitAsync(
            command.TeamAccountId,
            PlanLimitResourceType.Workflows,
            increment: 1,
            cancellationToken);
        if (planCheck.IsFailure)
        {
            if (planCheck.PlanLimitDetails is PlanLimitFailureDetails details)
                return Result<Guid>.PlanLimitFailure(details);
            return Result.Failure<Guid>(planCheck.ErrorCode!, planCheck.Error);
        }

        WorkflowExportDto data = command.ExportData;

        if (await workflowRepo.NameExistsAsync(data.Name, command.TeamAccountId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict,
                $"A workflow named '{data.Name}' already exists. Rename the import file and try again.");

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            data.Name, data.Description, command.TeamAccountId, command.CreatedBy);

        Dictionary<Guid, Guid> stepIdMap = ImportSteps(workflow, data.Steps);
        ImportTransitions(workflow, data.Transitions, stepIdMap);
        ImportTriggers(workflow, data.Triggers);

        await workflowRepo.AddAsync(workflow, cancellationToken);
        await referenceSync.SyncAsync(workflow, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RecordUsageDeltaAsync(
            command.TeamAccountId,
            PlanLimitResourceType.Workflows,
            delta: 1,
            cancellationToken);
        return workflow.Id;
    }

    private static Dictionary<Guid, Guid> ImportSteps(WorkflowDefinition workflow, IReadOnlyList<StepExportDto> steps)
    {
        Dictionary<Guid, Guid> idMap = new();

        WorkflowStep defaultStart = workflow.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep defaultEnd = workflow.Steps.Single(s => s.Type == StepType.End);

        foreach (StepExportDto stepDto in steps)
        {
            if (stepDto.Type == StepType.Start)
            {
                idMap[stepDto.Id] = defaultStart.Id;
                continue;
            }

            if (stepDto.Type == StepType.End)
            {
                idMap[stepDto.Id] = defaultEnd.Id;
                continue;
            }

            WorkflowStep newStep = workflow.AddStep(stepDto.Name, stepDto.Type, stepDto.Config);
            idMap[stepDto.Id] = newStep.Id;
        }

        return idMap;
    }

    private static void ImportTransitions(
        WorkflowDefinition workflow,
        IReadOnlyList<TransitionExportDto> transitions,
        Dictionary<Guid, Guid> stepIdMap)
    {
        foreach (TransitionExportDto t in transitions)
        {
            if (!stepIdMap.TryGetValue(t.FromStepId, out Guid from) ||
                !stepIdMap.TryGetValue(t.ToStepId, out Guid to))
                continue;

            try
            {
                workflow.AddTransition(from, to, t.Label);
            }
            catch (InvalidOperationException)
            {
                // Skip invalid transitions (e.g. cycles introduced by partial imports)
            }
        }
    }

    private static void ImportTriggers(WorkflowDefinition workflow, IReadOnlyList<TriggerExportDto> triggers)
    {
        foreach (TriggerExportDto t in triggers)
        {
            try
            {
                workflow.AddTrigger(t.Type, t.Config);
            }
            catch (InvalidOperationException)
            {
                // Skip duplicate trigger types
            }
        }
    }
}
