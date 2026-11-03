using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Expires the task a <c>task.deadline</c> work item names. The handler locks the task first, as a
/// completion does, so a task that ends one way cannot also end the other. A task that is still
/// <c>open</c> becomes <c>expired</c>, and its instance resumes on the <c>next</c> step of the
/// deadline in the task's pinned release. The step gets a history row whose decision is
/// <c>deadline</c>, the audit record <c>task.expired</c> is appended as <c>system</c>, and the
/// work item of the next step is queued. A task in any other state, or no task, is left alone and
/// the item is deleted. A failure is logged and the instance is not failed, since the handler can
/// fail only when the stored data is inconsistent.
/// </summary>
internal sealed partial class TaskDeadlineHandler(ReleaseModelCache models, ILogger<TaskDeadlineHandler> logger) : IWorkItemHandler
{
    /// <summary>The decision recorded in the history of the task step when the deadline expired the task.</summary>
    internal const string Decision = "deadline";

    public string Kind => ProcessStarts.TaskDeadlineWorkItemKind;

    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Item.TaskId is not { } taskId)
        {
            LogNoTask(context.Item.Id);
            return;
        }

        // The lock comes first. A completion that holds it commits before this reads the task.
        var task = await TaskQueries.LockAsync(context.Transaction, taskId, cancellationToken);
        if (task is not { State: ProcessTasks.Open })
        {
            LogNotOpen(taskId, task?.State);
            return;
        }

        var release = await models.GetAsync(context, task.ReleaseId, cancellationToken);
        var (_, step) = TaskCompletionCommand.StepOf(release, task);
        if (step.Deadline is not { } deadline)
        {
            LogNoDeadline(taskId, task.Step);
            return;
        }

        if (!await ProcessTasks.TryExpireAsync(context.Transaction, taskId, cancellationToken))
        {
            LogNotOpen(taskId, null);
            return;
        }

        // The escalations that have not run are moot. This item stays: the worker deletes it last.
        await ProcessTasks.DeletePendingEscalationsAsync(context.Transaction, taskId, cancellationToken);
        var revision = await ProcessSteps.TryResumeAsync(context.Transaction, task.ProcessInstanceId, task.Step, deadline.Next, null, cancellationToken)
            ?? throw new InvalidOperationException($"The instance of task '{taskId}' is not waiting on the task's step.");
        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(
                task.ProcessInstanceId,
                task.Step,
                revision,
                new JsonObject { ["subjectId"] = task.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["taskId"] = task.Id, ["next"] = deadline.Next },
                Decision,
                null,
                await RecordQueries.TransactionTimeAsync(context.Connection, cancellationToken)),
            cancellationToken);
        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.TaskExpired,
                task.ApplicationId,
                task.SubjectEntityId,
                task.SubjectId,
                task.ProcessInstanceId,
                new JsonObject { ["taskId"] = task.Id, ["step"] = task.Step, ["next"] = deadline.Next }),
            cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            context.Connection,
            context.Transaction,
            context.Item.TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: task.ProcessInstanceId);
    }

    public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        // The exception type only: its message can quote stored values.
        LogFailed(context.Item.Id, context.Item.TaskId, exception.GetType().Name);
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Warning, "Deadline work item {ItemId} names no task, so it was discarded.")]
    private partial void LogNoTask(Guid itemId);

    [LoggerMessage(LogLevel.Debug, "Task {TaskId} is missing or not open (state {State}), so its deadline item was discarded.")]
    private partial void LogNotOpen(Guid taskId, string? state);

    [LoggerMessage(LogLevel.Warning, "The step {Step} of task {TaskId} has no deadline in its release, so its deadline item was discarded.")]
    private partial void LogNoDeadline(Guid taskId, string step);

    [LoggerMessage(LogLevel.Error, "Deadline work item {ItemId} of task {TaskId} failed with {ErrorType}, so the task stays open.")]
    private partial void LogFailed(Guid itemId, Guid? taskId, string errorType);
}
