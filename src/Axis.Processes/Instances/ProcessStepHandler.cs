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
/// commits in the work item's transaction: an operation's record write, a task step's open human
/// task, a <c>callConnector</c> step's staged call, the new instance state and revision, a step
/// history row, its <c>system</c> audit records and the next work item. A task step leaves the
/// instance <c>waiting</c>, with no next work item. A <c>callConnector</c> step leaves it
/// <c>waiting</c> too, and queues the <c>connector.call</c> item of its call, so no request is sent
/// inside the transaction. A step that finds the instance at another revision than it loaded writes
/// nothing. A step that throws is rolled back, and the failure callback then records the error in
/// the history and marks the instance <c>failed</c>.
/// </summary>
internal sealed partial class ProcessStepHandler(ReleaseModelCache models, ILogger<ProcessStepHandler> logger) : IWorkItemHandler
{
    /// <summary>The decision recorded when no branch was true.</summary>
    internal const string Otherwise = "otherwise";

    private const string DateFormat = "yyyy-MM-dd";

    // PostgreSQL stores microseconds, so six fraction digits show every stored value exactly.
    private const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

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
        NewTask? task = null;
        NewConnectorCall? call = null;
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
            case TaskStepModel taskStep:
                var (assigneeKind, assignee) = await AssignAsync(context, model, entity, record, taskStep, instance, input, cancellationToken);
                task = new NewTask(
                    Guid.CreateVersion7(),
                    instance.Id,
                    instance.ProcessId,
                    instance.ReleaseId,
                    instance.ApplicationId,
                    step.Name,
                    instance.SubjectEntityId,
                    instance.SubjectId,
                    assigneeKind,
                    assignee,
                    taskStep.Form.Id,
                    taskStep.DueIn is { } dueIn ? instance.Now + dueIn : null);
                state = ProcessStarts.Waiting;
                nextStep = step.Name;
                decision = null;
                output = new JsonObject { ["state"] = ProcessStarts.Waiting, ["taskId"] = task.Id };
                details = new JsonObject { ["step"] = step.Name, ["taskId"] = task.Id };
                action = AuditActions.TaskCreated;
                break;
            case CallConnectorStepModel callStep:
                if (!process.TryGetStep(callStep.Next, out _))
                {
                    throw Failure($"The process '{process.Name}' has no step '{callStep.Next}'.", instance, input);
                }

                var (connector, operation) = FindOperation(model, callStep, instance, input);
                call = new NewConnectorCall(
                    Guid.CreateVersion7(),
                    instance.Id,
                    step.Name,
                    instance.ReleaseId,
                    connector.Name,
                    operation.Name,
                    await StageAsync(context, model, entity, record, callStep, operation, instance, input, cancellationToken));
                state = ProcessStarts.Waiting;
                nextStep = step.Name;
                decision = null;
                output = new JsonObject { ["state"] = ProcessStarts.Waiting, ["operationId"] = call.Id };
                details = new JsonObject
                {
                    ["step"] = step.Name,
                    ["connector"] = connector.Name,
                    ["operation"] = operation.Name,
                    ["operationId"] = call.Id,
                };
                action = AuditActions.ConnectorCallStaged;
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
            var result = await RecordCommands.SetAsync(context.Connection, model, entity, record, assignments, instance.Now, cancellationToken);
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

        if (task is not null)
        {
            await ProcessTasks.InsertAsync(context.Transaction, task, cancellationToken);
        }

        if (call is not null)
        {
            await ConnectorCalls.InsertAsync(context.Transaction, call, cancellationToken);
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

        if (call is not null)
        {
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.ConnectorCallWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                processInstanceId: instance.Id,
                connectorCallId: call.Id);
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
            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, branch.When, instance.Now, cancellationToken);
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

            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, assignment.Value, instance.Now, cancellationToken);
            if (result.Error is { } error)
            {
                throw Failure(error.Message, instance, input);
            }

            values.Add(KeyValuePair.Create(field, result.Value));
        }

        return values;
    }

    /// <summary>
    /// Resolves the assignee of a task step: the user id its <c>user</c> expression gives, or its role
    /// name. A <c>user</c> expression that fails at run time, or gives no non-blank text, fails the step.
    /// </summary>
    private static async Task<(string Kind, string Assignee)> AssignAsync(
        WorkItemContext context,
        ApplicationModel model,
        EntityModel entity,
        Record record,
        TaskStepModel step,
        LoadedInstance instance,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        if (step.Assignee.User is not { } user)
        {
            return (ProcessTasks.RoleAssignee, step.Assignee.Role!);
        }

        var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, user, instance.Now, cancellationToken);
        if (result.Error is { } error)
        {
            throw Failure(error.Message, instance, input);
        }

        if (result.Value is not string id || string.IsNullOrWhiteSpace(id))
        {
            throw Failure($"The assignee of the task step '{step.Name}' gave no user.", instance, input);
        }

        return (ProcessTasks.UserAssignee, id);
    }

    /// <summary>Finds the connector and the operation a <c>callConnector</c> step names, ignoring letter case.</summary>
    private static (ConnectorModel Connector, ConnectorOperationModel Operation) FindOperation(
        ApplicationModel model,
        CallConnectorStepModel step,
        LoadedInstance instance,
        JsonObject input)
    {
        var connector = model.Connectors.FirstOrDefault(candidate => string.Equals(candidate.Name, step.Connector, StringComparison.OrdinalIgnoreCase))
            ?? throw Failure($"The release has no connector '{step.Connector}'.", instance, input);
        var operation = connector.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, step.Operation, StringComparison.OrdinalIgnoreCase))
            ?? throw Failure($"The connector '{connector.Name}' has no operation '{step.Operation}'.", instance, input);
        return (connector, operation);
    }

    /// <summary>
    /// Evaluates each input of a <c>callConnector</c> step, in the step's order, into the body the
    /// call stores. Dates and date-times take the record API's text forms, and an optional input
    /// that gives null is stored as null. An input that fails at run time, or a required input that
    /// gives null, fails the step.
    /// </summary>
    private static async Task<JsonObject> StageAsync(
        WorkItemContext context,
        ApplicationModel model,
        EntityModel entity,
        Record record,
        CallConnectorStepModel step,
        ConnectorOperationModel operation,
        LoadedInstance instance,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        foreach (var mapping in step.Input)
        {
            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, mapping.Value, instance.Now, cancellationToken);
            if (result.Error is { } error)
            {
                throw Failure(error.Message, instance, input);
            }

            var required = operation.Input.Any(field => field.Required && string.Equals(field.Name, mapping.Input, StringComparison.OrdinalIgnoreCase));
            if (result.Value is null && required)
            {
                throw Failure($"The required input '{mapping.Input}' of the step '{step.Name}' gave no value.", instance, input);
            }

            body[mapping.Input] = result.Value switch
            {
                null => null,
                string text => JsonValue.Create(text),
                long number => JsonValue.Create(number),
                decimal number => JsonValue.Create(number),
                bool flag => JsonValue.Create(flag),
                DateOnly date => JsonValue.Create(date.ToString(DateFormat, CultureInfo.InvariantCulture)),
                DateTimeOffset time => JsonValue.Create(time.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
                Guid id => JsonValue.Create(id.ToString("D")),
                var other => throw Failure($"The input '{mapping.Input}' of the step '{step.Name}' gave a value of type '{other.GetType().Name}'.", instance, input),
            };
        }

        return body;
    }

    /// <summary>Each error of a rejected record write as its pointer and messages, such as <c>/values/amount: request.amountNegative</c>, or the outcome when it has none.</summary>
    internal static string Describe(RecordWriteResult result) =>
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
