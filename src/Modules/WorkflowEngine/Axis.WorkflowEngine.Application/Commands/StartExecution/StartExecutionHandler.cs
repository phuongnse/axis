using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.ReadModels;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Application.Commands.StartExecution;

/// <summary>Validates workflow is active, creates Pending execution with all steps,
/// then dispatches ExecuteNextStepMessage so Wolverine picks up execution asynchronously.</summary>
public sealed class StartExecutionHandler(
    IPlanLimitService planLimitService,
    IExecutionRepository execRepo,
    IWorkflowDefinitionReader workflowReader,
    IUnitOfWork uow,
    IStepDispatcher dispatcher,
    ILogger<StartExecutionHandler> logger)
    : ICommandHandler<StartExecutionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(StartExecutionCommand command, CancellationToken cancellationToken)
    {
        Result planCheck = await planLimitService.EnsureWithinLimitAsync(
            command.tenantId,
            PlanLimitResourceType.ExecutionsPerMonth,
            increment: 1,
            cancellationToken);
        if (planCheck.IsFailure)
        {
            if (planCheck.PlanLimitDetails is PlanLimitFailureDetails details)
                return Result<Guid>.PlanLimitFailure(details);
            return Result.Failure<Guid>(planCheck.ErrorCode!, planCheck.Error);
        }

        // only Active workflows can be triggered
        if (!await workflowReader.IsActiveAsync(command.WorkflowDefinitionId, command.tenantId, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.BusinessRule,
                "This workflow cannot be triggered. Only active workflows can be executed.");

        WorkflowSnapshot? snapshot = await workflowReader.GetSnapshotAsync(
            command.WorkflowDefinitionId, command.tenantId, cancellationToken);

        if (snapshot is null)
        {
            logger.LogError(
                "No workflow snapshot found for workflow {WorkflowId} in Tenant {TenantId}",
                command.WorkflowDefinitionId, command.tenantId);
            return Result.Failure<Guid>(ErrorCodes.BusinessRule,
                "Workflow definition snapshot not found. Please re-publish the workflow.");
        }

        WorkflowExecution execution = WorkflowExecution.Create(
            command.WorkflowDefinitionId,
            command.tenantId,
            command.TriggerType,
            command.TriggeredByUserId,
            command.Input ?? new Dictionary<string, object?>());

        execution.InitialiseSteps(snapshot.Steps);

        await execRepo.AddAsync(execution, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RecordUsageDeltaAsync(
            command.tenantId,
            PlanLimitResourceType.ExecutionsPerMonth,
            delta: 1,
            cancellationToken);

        // Dispatch async — Wolverine picks up execution outside this request/transaction boundary
        await dispatcher.PublishAsync(
            new ExecuteNextStepMessage(execution.Id, execution.tenantId), cancellationToken);

        return execution.Id;
    }
}
