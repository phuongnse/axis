using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs a <c>process.timer</c> work item: it resumes an instance that waits on a <c>timer</c> step once
/// the item is due. The item names the revision the timer step left, and the instance goes on to the
/// step's <c>next</c> only while it still waits at that revision on a timer step. The resume writes the
/// step's history row with <c>{ "next" }</c>, the <c>process.stepCompleted</c> audit record and the
/// instance's next <c>process.step</c> item in one transaction. An item whose instance moved on, was
/// cancelled or is gone is deleted with no other write. A handler that throws is rolled back, and the
/// failure callback then marks the waiting instance <c>failed</c> with a history error, so the deleted
/// item does not leave the instance waiting for ever.
/// </summary>
internal sealed partial class ProcessTimerHandler(ReleaseModelCache models, ILogger<ProcessTimerHandler> logger) : IWorkItemHandler
{
    public string Kind => ProcessStarts.TimerWorkItemKind;

    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Item.ProcessInstanceId is not { } instanceId || context.Item.Revision is not { } revision)
        {
            LogNoInstance(context.Item.Id);
            return;
        }

        var instance = await ProcessSteps.LoadAsync(context.Transaction, instanceId, cancellationToken);
        if (instance is not { State: ProcessStarts.Waiting } || instance.Revision != revision)
        {
            LogStale(instanceId, revision, instance?.State, instance?.Revision);
            return;
        }

        var model = await models.GetAsync(context, instance.ReleaseId, cancellationToken);
        var process = model.Processes.Single(candidate => candidate.Id == instance.ProcessId);
        if (!process.TryGetStep(instance.Step, out var step) || step is not TimerStepModel timer)
        {
            LogNotTimer(instanceId, instance.Step);
            return;
        }

        if (!await ProcessSteps.TryResumeCallAsync(context.Transaction, instanceId, instance.Step, revision, timer.Next, cancellationToken))
        {
            LogStale(instanceId, revision, instance.State, instance.Revision);
            return;
        }

        var input = new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null };
        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(instanceId, instance.Step, revision, input, new JsonObject { ["next"] = timer.Next }, null, null, instance.Now),
            cancellationToken);
        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessStepCompleted,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instanceId,
                new JsonObject { ["step"] = instance.Step, ["next"] = timer.Next }),
            cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            context.Connection,
            context.Transaction,
            context.Item.TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instanceId);
    }

    public async Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);
        if (context.Item.ProcessInstanceId is not { } instanceId
            || context.Item.Revision is not { } revision
            || await ProcessSteps.LoadAsync(context.Transaction, instanceId, cancellationToken) is not { State: ProcessStarts.Waiting } instance
            || instance.Revision != revision)
        {
            return;
        }

        // The item is deleted, so an instance that stayed waiting would never be resumed.
        if (!await ProcessSteps.TryFailWaitingAsync(context.Transaction, instanceId, instance.Step, revision, cancellationToken))
        {
            return;
        }

        await InstanceMetrics.RecordEndedAsync(models, context, instance, ProcessStarts.Failed, cancellationToken);
        var error = exception.GetType().FullName ?? exception.GetType().Name;
        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(
                instanceId,
                instance.Step,
                revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                null,
                null,
                error,
                instance.Now),
            cancellationToken);

        // The same fixed text the history row stores. It holds names and the error kind, never a value.
        LogFailed(instanceId, instance.Step, error);
        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessFailed,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instanceId,
                new JsonObject { ["step"] = instance.Step }),
            cancellationToken);
    }

    [LoggerMessage(LogLevel.Warning, "Timer work item {ItemId} names no process instance or no revision, so it was discarded.")]
    private partial void LogNoInstance(Guid itemId);

    [LoggerMessage(LogLevel.Information, "Timer of process instance {InstanceId} was set at revision {Revision}, but the instance is {State} at revision {CurrentRevision}, so the item was discarded.")]
    private partial void LogStale(Guid instanceId, long revision, string? state, long? currentRevision);

    [LoggerMessage(LogLevel.Warning, "Process instance {InstanceId} waits on step {Step}, which is not a timer step, so its timer work item was discarded.")]
    private partial void LogNotTimer(Guid instanceId, string step);

    [LoggerMessage(LogLevel.Warning, "Timer step {Step} of process instance {InstanceId} failed: {Error}")]
    private partial void LogFailed(Guid instanceId, string step, string error);
}
