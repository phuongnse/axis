using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs the escalation a <c>task.escalate</c> work item names. The handler locks the task first,
/// as a completion does, so a task that ends cannot also be escalated. A task that is still
/// <c>open</c> is reassigned to the escalation's <c>reassign</c> target as <c>system</c>, which clears
/// a claim. The audit record <c>task.reassigned</c> is appended with the escalation's index. The
/// escalation's <c>notify</c> is evaluated as a <c>notify</c> step evaluates it and stored as an in-app
/// notification, in the same transaction. The task step gets a history row whose output is
/// <c>{ "escalation": index }</c>, with the <c>notificationId</c> when there is a notify. The task
/// stays open and its instance is not changed. A task in any other state, or no task, is left alone and
/// the item is deleted. A task whose <c>user</c> expression gives no user is left as it is, with a
/// warning, since a late reassign must not fail an instance that waits correctly, but its notify still
/// runs. A failure, such as a notify expression that fails, is logged, the whole escalation is rolled
/// back and the instance is not failed.
/// </summary>
internal sealed partial class TaskEscalationHandler(ReleaseModelCache models, ILogger<TaskEscalationHandler> logger) : IWorkItemHandler
{
    public string Kind => ProcessStarts.TaskEscalateWorkItemKind;

    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Item is not { TaskId: { } taskId, Escalation: { } index })
        {
            LogNoEscalation(context.Item.Id);
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
        if (index < 0 || index >= step.Escalations.Count)
        {
            LogNoSuchEscalation(taskId, index, task.Step);
            return;
        }

        var escalation = step.Escalations[index];
        var evaluate = await SubjectEvaluatorAsync(context, release, task, cancellationToken);

        NewNotification? notification = null;
        if (escalation.Notify is { } notify)
        {
            if (evaluate is null)
            {
                LogNoSubject(taskId, index);
            }
            else
            {
                notification = await ProcessNotifications.EvaluateAsync(
                    notify,
                    evaluate,
                    task.ApplicationId,
                    task.ReleaseId,
                    (part, error) => new InvalidOperationException($"The {part} of escalation {index} of the step '{task.Step}' failed: {error.Kind}."),
                    (value, name) => ProcessNotifications.TryToJson(value, out var json)
                        ? json
                        : throw new InvalidOperationException($"The {name} of escalation {index} of the step '{task.Step}' gave a value of type '{value!.GetType().Name}'."));
            }
        }

        string? assigneeKind = null;
        string? assignee = null;
        if (escalation.Reassign is { } reassign)
        {
            if (reassign.User is { } user)
            {
                if (await UserAsync(evaluate, user) is { } id)
                {
                    (assigneeKind, assignee) = (ProcessTasks.UserAssignee, id);
                }
                else
                {
                    LogNoUser(taskId, index);
                }
            }
            else
            {
                (assigneeKind, assignee) = (ProcessTasks.RoleAssignee, reassign.Role!);
            }
        }

        var reassigned = false;
        if (assigneeKind is not null && assignee is not null)
        {
            if (!await ProcessTasks.TryReassignAsync(context.Transaction, taskId, assigneeKind, assignee, cancellationToken))
            {
                LogNotOpen(taskId, null);
                return;
            }

            reassigned = true;
        }

        if (!reassigned && notification is null)
        {
            return;
        }

        if (notification is not null && ProcessNotifications.ShouldStore(notification, escalation.Notify!.Channels))
        {
            await ProcessNotifications.InsertAsync(context.Transaction, notification, cancellationToken);
        }

        if (reassigned)
        {
            await AuditRecords.AppendAsync(
                context.Transaction,
                new AuditEntry(
                    AuditActors.System,
                    AuditActions.TaskReassigned,
                    task.ApplicationId,
                    task.SubjectEntityId,
                    task.SubjectId,
                    task.ProcessInstanceId,
                    new JsonObject
                    {
                        ["taskId"] = task.Id,
                        ["from"] = new JsonObject { [task.AssigneeKind] = task.Assignee },
                        ["to"] = new JsonObject { [assigneeKind!] = assignee },
                        ["escalation"] = index,
                    }),
                cancellationToken);
        }

        var output = new JsonObject { ["escalation"] = index };
        if (notification is not null)
        {
            output["notificationId"] = notification.Id;
        }

        // The instance keeps waiting on the step, so the row takes its current revision.
        var instance = await ProcessSteps.LoadAsync(context.Transaction, task.ProcessInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"The instance of task '{taskId}' is missing.");
        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(
                task.ProcessInstanceId,
                task.Step,
                instance.Revision,
                new JsonObject { ["subjectId"] = task.SubjectId, ["subjectVersion"] = null },
                output,
                null,
                null,
                await RecordQueries.TransactionTimeAsync(context.Connection, cancellationToken)),
            cancellationToken);
    }

    public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        // The exception type only: its message can quote stored values.
        LogFailed(context.Item.Id, context.Item.TaskId, exception.GetType().Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A function that evaluates an expression over the task's subject record, as a task step evaluates
    /// its expressions, or <see langword="null"/> when the record is missing. The record has its pending
    /// computed fields computed by the active release, and the time is the transaction's.
    /// </summary>
    private async Task<Func<ExpressionModel, Task<ExpressionEvaluationResult>>?> SubjectEvaluatorAsync(
        WorkItemContext context,
        ApplicationModel release,
        ProcessTaskRow task,
        CancellationToken cancellationToken)
    {
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        if (await RecordQueries.GetAsync(context.Connection, release, entity, task.SubjectId, cancellationToken) is not { } stored)
        {
            return null;
        }

        // The expression reads the record with its pending computed fields computed by the active release, as a step's does.
        var active = await models.FindActiveAsync(context, task.ApplicationId, cancellationToken);
        var subject = active is { } current
            ? await PendingComputedReads.ApplyAsync(context.Connection, context.Transaction, current.Model, entity, stored, cancellationToken, release)
            : stored;
        var now = await RecordQueries.TransactionTimeAsync(context.Connection, cancellationToken);
        return expression => RecordExpressions.EvaluateAsync(context.Connection, release, entity, subject, expression, now, cancellationToken);
    }

    /// <summary>
    /// The user id a <c>reassign</c> expression gives, or <see langword="null"/> when the record is
    /// missing or the expression fails or gives no non-blank text.
    /// </summary>
    private static async Task<string?> UserAsync(Func<ExpressionModel, Task<ExpressionEvaluationResult>>? evaluate, ExpressionModel user) =>
        evaluate is not null && await evaluate(user) is { Error: null, Value: string id } && !string.IsNullOrWhiteSpace(id) ? id : null;

    [LoggerMessage(LogLevel.Warning, "The subject record of task {TaskId} is missing, so the notify of escalation {Index} was skipped.")]
    private partial void LogNoSubject(Guid taskId, int index);

    [LoggerMessage(LogLevel.Warning, "Escalation work item {ItemId} names no task or escalation, so it was discarded.")]
    private partial void LogNoEscalation(Guid itemId);

    [LoggerMessage(LogLevel.Debug, "Task {TaskId} is missing or not open (state {State}), so its escalation item was discarded.")]
    private partial void LogNotOpen(Guid taskId, string? state);

    [LoggerMessage(LogLevel.Warning, "The step {Step} of task {TaskId} has no escalation {Index} in its release, so its escalation item was discarded.")]
    private partial void LogNoSuchEscalation(Guid taskId, int index, string step);

    [LoggerMessage(LogLevel.Warning, "The reassign of escalation {Index} of task {TaskId} gave no user, so the task was left as it is.")]
    private partial void LogNoUser(Guid taskId, int index);

    [LoggerMessage(LogLevel.Error, "Escalation work item {ItemId} of task {TaskId} failed with {ErrorType}, so the task stays as it is.")]
    private partial void LogFailed(Guid itemId, Guid? taskId, string errorType);
}
