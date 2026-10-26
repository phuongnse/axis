using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Resumes the parent of the child a <c>process.resume</c> work item names. The end step of a child
/// queues the item. The handler goes on only when the child is <c>completed</c> and has a parent, the
/// parent is <c>waiting</c> on the <c>subProcess</c> step that started the child, and the latest history
/// row of that step names the child as its <c>childInstanceId</c>. The parent then moves to the step's
/// <c>next</c> in its pinned release. The step gets a history row with the output <c>next</c> and
/// <c>childInstanceId</c>, the audit record <c>process.stepCompleted</c> is appended as <c>system</c>,
/// and the work item of the next step is queued. Any other case is logged and the item is deleted.
/// A failure is logged and the parent is not failed, since the handler can fail only when the stored
/// data is inconsistent.
/// </summary>
internal sealed partial class ProcessResumeHandler(ReleaseModelCache models, ILogger<ProcessResumeHandler> logger) : IWorkItemHandler
{
    public string Kind => ProcessStarts.ProcessResumeWorkItemKind;

    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Item.ProcessInstanceId is not { } childId)
        {
            LogNoChild(context.Item.Id);
            return;
        }

        var child = await ProcessSteps.LoadAsync(context.Transaction, childId, cancellationToken);
        if (child is not { State: ProcessStarts.Completed, ParentInstanceId: { } parentId, ParentStep: { } parentStep })
        {
            LogChildNotResumable(childId, child?.State);
            return;
        }

        var parent = await ProcessSteps.LoadAsync(context.Transaction, parentId, cancellationToken);
        if (parent is not { State: ProcessStarts.Waiting } || !string.Equals(parent.Step, parentStep, StringComparison.Ordinal))
        {
            LogParentNotWaiting(parentId, childId, parent?.State);
            return;
        }

        var waiting = await ProcessSteps.FindLatestOutputAsync(context.Transaction, parentId, parentStep, cancellationToken);
        if (!Guid.TryParse(waiting?["childInstanceId"]?.GetValue<string>(), out var waitedFor) || waitedFor != childId)
        {
            LogOtherChild(parentId, parentStep, childId);
            return;
        }

        var release = await models.GetAsync(context, parent.ReleaseId, cancellationToken);
        if (!release.Processes.Single(candidate => candidate.Id == parent.ProcessId).TryGetStep(parentStep, out var step) || step is not SubProcessStepModel subProcess)
        {
            LogNotSubProcess(parentId, parentStep);
            return;
        }

        if (!await ProcessSteps.TryResumeCallAsync(context.Transaction, parentId, parentStep, parent.Revision, subProcess.Next, cancellationToken))
        {
            LogStale(parentId, parentStep, parent.Revision);
            return;
        }

        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(
                parentId,
                parentStep,
                parent.Revision,
                new JsonObject { ["subjectId"] = parent.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["next"] = subProcess.Next, ["childInstanceId"] = childId },
                null,
                null,
                parent.Now),
            cancellationToken);
        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessStepCompleted,
                parent.ApplicationId,
                parent.SubjectEntityId,
                parent.SubjectId,
                parentId,
                new JsonObject { ["step"] = parentStep, ["next"] = subProcess.Next }),
            cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            context.Connection,
            context.Transaction,
            context.Item.TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: parentId);
    }

    public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        // The exception type only: its message can quote stored values.
        LogFailed(context.Item.Id, context.Item.ProcessInstanceId, exception.GetType().Name);
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Warning, "Resume work item {ItemId} names no process instance, so it was discarded.")]
    private partial void LogNoChild(Guid itemId);

    [LoggerMessage(LogLevel.Warning, "Process instance {ChildId} is missing, not completed (state {State}) or has no parent, so its resume item was discarded.")]
    private partial void LogChildNotResumable(Guid childId, string? state);

    [LoggerMessage(LogLevel.Warning, "Parent {ParentId} of process instance {ChildId} is missing or not waiting on the sub-process step (state {State}), so the resume item was discarded.")]
    private partial void LogParentNotWaiting(Guid parentId, Guid childId, string? state);

    [LoggerMessage(LogLevel.Warning, "Parent {ParentId} waits on step {Step} for another child than {ChildId}, so the resume item was discarded.")]
    private partial void LogOtherChild(Guid parentId, string step, Guid childId);

    [LoggerMessage(LogLevel.Warning, "Step {Step} of parent {ParentId} is not a sub-process step in its release, so the resume item was discarded.")]
    private partial void LogNotSubProcess(Guid parentId, string step);

    [LoggerMessage(LogLevel.Warning, "Parent {ParentId} changed since revision {Revision} of step {Step}, so the resume item was discarded.")]
    private partial void LogStale(Guid parentId, string step, long revision);

    [LoggerMessage(LogLevel.Error, "Resume work item {ItemId} of process instance {ChildId} failed with {ErrorType}, so the parent stays waiting.")]
    private partial void LogFailed(Guid itemId, Guid? childId, string errorType);
}
