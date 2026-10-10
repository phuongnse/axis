using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs the current step of a process instance on the release the instance is pinned to. The step
/// commits in the work item's transaction: an operation's record write, the new instance state and
/// revision, a step history row, its <c>system</c> audit records and the next work item. A step
/// that finds the instance at another revision than it loaded writes nothing. A step that throws is
/// rolled back, and the failure callback then records the error in the history and marks the
/// instance <c>failed</c>.
/// </summary>
internal sealed partial class ProcessStepHandler(ReleaseModelCache models, ILogger<ProcessStepHandler> logger) : IWorkItemHandler
{
    /// <summary>The decision recorded when no branch was true.</summary>
    internal const string Otherwise = "otherwise";

    public string Kind => ProcessStarts.StepWorkItemKind;

    public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Item.ProcessInstanceId is not { } instanceId)
        {
            LogNoInstance(context.Item.Id);
            return;
        }

        var instance = await ProcessSteps.LoadAsync(context.Transaction, instanceId, cancellationToken);
        if (instance is not { State: ProcessStarts.Running })
        {
            LogNotRunning(instanceId, instance?.State);
            return;
        }

        var input = new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null };
        try
        {
            await RunStepAsync(context, instance, input, cancellationToken);
        }
        catch (Exception exception) when (exception is not ProcessStepException and not OperationCanceledException)
        {
            // Any failure after the load fails the instance at the revision the step loaded.
            throw Failure(exception.Message, instance, input, exception);
        }
    }

    public async Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);
        if (context.Item.ProcessInstanceId is not { } instanceId
            || await ProcessSteps.LoadAsync(context.Transaction, instanceId, cancellationToken) is not { State: ProcessStarts.Running } instance)
        {
            return;
        }

        var failure = exception as ProcessStepException;
        var revision = failure?.Revision ?? instance.Revision;
        var step = failure?.Step ?? instance.Step;
        if (!await ProcessSteps.TryFailAsync(context.Transaction, instanceId, revision, cancellationToken))
        {
            LogStaleRevision(instanceId, step, revision);
            return;
        }

        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(instanceId, step, revision, failure?.Input ?? new JsonObject(), null, null, exception.Message, failure?.StartedAt ?? instance.Now),
            cancellationToken);
        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessFailed,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instanceId,
                new JsonObject { ["step"] = step }),
            cancellationToken);
    }

    private async Task RunStepAsync(WorkItemContext context, LoadedInstance instance, JsonObject input, CancellationToken cancellationToken)
    {
        var model = await models.GetAsync(context, instance.ReleaseId, cancellationToken);
        var process = model.Processes.Single(candidate => candidate.Id == instance.ProcessId);
        var entity = model.FindEntity(instance.SubjectEntityId)
            ?? throw Failure($"The release has no entity '{instance.SubjectEntityId}'.", instance, input);
        if (!process.TryGetStep(instance.Step, out var step))
        {
            throw Failure($"The process '{process.Name}' has no step '{instance.Step}'.", instance, input);
        }

        if (step is OperationStepModel)
        {
            // The lock makes the read below see the latest version, and a concurrent write of the
            // record waits until this step commits or rolls back.
            await RecordCommands.LockAsync(context.Connection, entity, instance.SubjectId, cancellationToken);
        }

        var record = await RecordQueries.GetAsync(context.Connection, model, entity, instance.SubjectId, cancellationToken)
            ?? throw Failure($"No record of '{entity.Name}' has the id '{instance.SubjectId}'.", instance, input);
        input["subjectVersion"] = record.Version;

        string state;
        string nextStep;
        string? decision;
        JsonObject output;
        JsonObject details;
        string action;
        List<KeyValuePair<FieldModel, object?>>? assignments = null;
        switch (step)
        {
            case DecisionStepModel decisionStep:
                (decision, var target) = await DecideAsync(context, model, entity, record, decisionStep, instance, input, cancellationToken);
                if (!process.TryGetStep(target, out var next))
                {
                    throw Failure($"The process '{process.Name}' has no step '{target}'.", instance, input);
                }

                state = ProcessStarts.Running;
                nextStep = next.Name;
                output = new JsonObject { ["next"] = next.Name };
                details = new JsonObject { ["step"] = step.Name, ["next"] = next.Name };
                action = AuditActions.ProcessStepCompleted;
                break;
            case OperationStepModel operationStep:
                if (!process.TryGetStep(operationStep.Next, out var after))
                {
                    throw Failure($"The process '{process.Name}' has no step '{operationStep.Next}'.", instance, input);
                }

                assignments = await EvaluateAsync(context, model, entity, record, operationStep, instance, input, cancellationToken);
                state = ProcessStarts.Running;
                nextStep = after.Name;
                decision = null;
                output = new JsonObject { ["next"] = after.Name };
                details = new JsonObject { ["step"] = step.Name, ["next"] = after.Name };
                action = AuditActions.ProcessStepCompleted;
                break;
            case EndStepModel:
                state = ProcessStarts.Completed;
                nextStep = step.Name;
                decision = null;
                output = new JsonObject { ["state"] = ProcessStarts.Completed };
                details = new JsonObject { ["step"] = step.Name };
                action = AuditActions.ProcessCompleted;
                break;
            default:
                throw Failure($"The engine cannot run the step '{step.Name}' of type '{step.GetType().Name}'.", instance, input);
        }

        if (!await ProcessSteps.TryAdvanceAsync(context.Transaction, instance.Id, instance.Revision, state, nextStep, cancellationToken))
        {
            // The newer revision owns the instance now, so this item is discarded with no other write.
            LogStaleRevision(instance.Id, step.Name, instance.Revision);
            return;
        }

        // The record is written only once the instance is known to be current, so a stale step
        // writes nothing. A rejected write throws, which rolls back the instance's move too.
        AuditEntry? recordAudit = null;
        if (assignments is not null)
        {
            var result = await RecordCommands.SetAsync(context.Connection, model, entity, record, assignments, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                throw Failure($"The record update was rejected: {Describe(result)}", instance, input);
            }

            output["subjectVersion"] = written.Version;
            recordAudit = new AuditEntry(
                AuditActors.System,
                AuditActions.RecordUpdated,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject
                {
                    ["version"] = written.Version,
                    ["fields"] = new JsonArray([.. assignments.Select(assignment => JsonValue.Create(assignment.Key.Name))]),
                });
        }

        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(instance.Id, step.Name, instance.Revision, input, output, decision, null, instance.Now),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(context.Transaction, recordAudit, cancellationToken);
        }

        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(AuditActors.System, action, instance.ApplicationId, instance.SubjectEntityId, instance.SubjectId, instance.Id, details),
            cancellationToken);
        if (state == ProcessStarts.Running)
        {
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.StepWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                processInstanceId: instance.Id);
        }
    }

    /// <summary>
    /// Takes the first branch whose <c>when</c> is true, or <c>otherwise</c>. A <c>when</c> that
    /// gives null counts as false, and one that fails at run time fails the step.
    /// </summary>
    private static async Task<(string Decision, string Next)> DecideAsync(
        WorkItemContext context,
        ApplicationModel model,
        EntityModel entity,
        Record record,
        DecisionStepModel step,
        LoadedInstance instance,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < step.Branches.Count; index++)
        {
            var branch = step.Branches[index];
            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, branch.When, cancellationToken);
            if (result.Error is { } error)
            {
                throw Failure(error.Message, instance, input);
            }

            if (result.Value is true)
            {
                return (index.ToString(CultureInfo.InvariantCulture), branch.Next);
            }
        }

        return (Otherwise, step.Otherwise);
    }

    /// <summary>
    /// Evaluates the value of each field an <c>updateRecord</c> operation sets, in the entity's
    /// declaration order. A value that fails at run time fails the step.
    /// </summary>
    private static async Task<List<KeyValuePair<FieldModel, object?>>> EvaluateAsync(
        WorkItemContext context,
        ApplicationModel model,
        EntityModel entity,
        Record record,
        OperationStepModel step,
        LoadedInstance instance,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var assignment in step.Set)
        {
            if (!entity.TryGetField(assignment.Field, out var field))
            {
                throw Failure($"The entity '{entity.Name}' has no field '{assignment.Field}'.", instance, input);
            }

            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, assignment.Value, cancellationToken);
            if (result.Error is { } error)
            {
                throw Failure(error.Message, instance, input);
            }

            values.Add(KeyValuePair.Create(field, result.Value));
        }

        return values;
    }

    /// <summary>Each error of a rejected record write as its pointer and messages, such as <c>/values/amount: request.amountNegative</c>, or the outcome when it has none.</summary>
    private static string Describe(RecordWriteResult result) =>
        result.Errors is { Count: > 0 } errors
            ? string.Join(", ", errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}"))
            : result.Outcome.ToString();

    private static ProcessStepException Failure(string message, LoadedInstance instance, JsonObject input, Exception? innerException = null) =>
        new(message, instance.Step, instance.Revision, instance.Now, input, innerException);

    [LoggerMessage(LogLevel.Warning, "Step work item {ItemId} names no process instance, so it was discarded.")]
    private partial void LogNoInstance(Guid itemId);

    [LoggerMessage(LogLevel.Warning, "Process instance {InstanceId} is missing or not running (state {State}), so its step work item was discarded.")]
    private partial void LogNotRunning(Guid instanceId, string? state);

    [LoggerMessage(LogLevel.Warning, "Step {Step} of process instance {InstanceId} ran at revision {Revision}, but the instance changed since, so the step wrote nothing.")]
    private partial void LogStaleRevision(Guid instanceId, string step, long revision);
}
