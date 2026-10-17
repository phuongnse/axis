using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs the current step of a process instance on the release the instance is pinned to. The step
/// commits in the work item's transaction: an operation's record write, a task step's open human
/// task, a <c>callConnector</c> step's staged call, a <c>raiseEvent</c> step's event occurrence,
/// the instances its <c>sync</c> event triggers start and the <c>event.deliver</c> item that
/// delivers it to its <c>async</c> triggers, the new instance state and revision, a step
/// history row, its <c>system</c> audit records and the next work item. A task step leaves the
/// instance <c>waiting</c>, with no next work item. A <c>callConnector</c> step leaves it
/// <c>waiting</c> too, and queues the <c>connector.call</c> item of its call, so no request is sent
/// inside the transaction. A package operation step does the same with a <c>package.call</c> item,
/// so no operation runs inside the transaction. An <c>updateRecord</c> step starts the data change
/// triggers of the update after its <c>record.updated</c> audit. A <c>sync</c> trigger and a data
/// change trigger come from the application's active release, and
/// its start runs as <c>system</c>, pinned to that release. A skipped <c>sync</c> start is logged
/// and lets the step commit. A step that finds the instance at another revision than it loaded
/// writes nothing. An operation step and a <c>raiseEvent</c> step take the application's start lock
/// shared before they read an entity table, since they can start instances. A step that throws is rolled back, and the failure callback then records the
/// error in the history and marks the instance <c>failed</c>.
/// </summary>
internal sealed partial class ProcessStepHandler(ReleaseModelCache models, PackageCatalog packages, ILogger<ProcessStepHandler> logger) : IWorkItemHandler
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
            throw Failure(TypeName(exception), instance, input, exception);
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
            new StepOccurrence(instanceId, step, revision, failure?.Input ?? new JsonObject(), null, null, failure?.Message ?? TypeName(exception), failure?.StartedAt ?? instance.Now),
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
        using var activity = AxisTelemetry.Source.StartActivity(
            AxisTelemetry.ProcessStep,
            ActivityKind.Internal,
            default(ActivityContext),
            [
                new(AxisTelemetry.TenantTag, context.Item.TenantId),
                new(AxisTelemetry.ApplicationTag, model.Manifest.Name),
                new(AxisTelemetry.ProcessTag, process.Name),
                new(AxisTelemetry.StepTag, instance.Step),
            ]);
        try
        {
            await RunStepCoreAsync(context, instance, input, model, process, cancellationToken);
        }
        catch (Exception exception)
        {
            AxisTelemetry.RecordFailure(activity, exception);
            throw;
        }
    }

    private async Task RunStepCoreAsync(WorkItemContext context, LoadedInstance instance, JsonObject input, ApplicationModel model, ProcessModel process, CancellationToken cancellationToken)
    {
        var entity = model.FindEntity(instance.SubjectEntityId)
            ?? throw Failure($"The release has no entity '{instance.SubjectEntityId}'.", instance, input);
        if (!process.TryGetStep(instance.Step, out var step))
        {
            throw Failure($"The process '{process.Name}' has no step '{instance.Step}'.", instance, input);
        }

        if (step is OperationStepModel or RaiseEventStepModel)
        {
            // These steps can start an instance: an operation through a data change trigger and a raised
            // event through a sync trigger. The start lock comes before any entity table is read, so a
            // migrating activation cannot deadlock with the step, and the active release read later is
            // read under it. The step keeps the release the instance is pinned to.
            await StartLock.TakeSharedAsync(context.Transaction, instance.ApplicationId, cancellationToken);
        }

        if (step is OperationStepModel)
        {
            // The lock makes the read below see the latest version, and a concurrent write of the
            // record waits until this step commits or rolls back.
            await RecordCommands.LockAsync(context.Connection, entity, instance.SubjectId, cancellationToken);
        }

        var stored = await RecordQueries.GetAsync(context.Connection, model, entity, instance.SubjectId, cancellationToken)
            ?? throw Failure($"No record of '{entity.Name}' has the id '{instance.SubjectId}'.", instance, input);
        input["subjectVersion"] = stored.Version;

        // The step's expressions read the record with its pending computed fields computed by the active release,
        // whatever release the instance is pinned to. The write of an updateRecord step starts from the stored record.
        var active = await models.FindActiveAsync(context, instance.ApplicationId, cancellationToken);
        var record = active is { } release
            ? await PendingComputedReads.ApplyAsync(context.Connection, context.Transaction, release.Model, entity, stored, cancellationToken, model)
            : stored;

        string state;
        string nextStep;
        string? decision;
        JsonObject output;
        JsonObject details;
        string action;
        List<KeyValuePair<FieldModel, object?>>? assignments = null;
        NewTask? task = null;
        NewConnectorCall? call = null;
        NewPackageCall? packageCall = null;
        var restagesCall = false;
        var restagesPackageCall = false;
        NewEvent? occurrence = null;
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

                // An operator retry leaves the failed call of this step. It is staged again under its id.
                var failedCallId = await ConnectorCalls.FindFailedAsync(context.Transaction, instance.Id, step.Name, cancellationToken);
                restagesCall = failedCallId is not null;
                call = new NewConnectorCall(
                    failedCallId ?? Guid.CreateVersion7(),
                    instance.Id,
                    step.Name,
                    instance.ReleaseId,
                    connector.Name,
                    operation.Name,
                    await StageAsync(
                        context,
                        model,
                        entity,
                        record,
                        callStep,
                        callStep.Input,
                        name => operation.Input.Any(field => field.Required && string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)),
                        instance,
                        input,
                        cancellationToken));
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
            case RaiseEventStepModel raiseStep:
                if (!process.TryGetStep(raiseStep.Next, out var following))
                {
                    throw Failure($"The process '{process.Name}' has no step '{raiseStep.Next}'.", instance, input);
                }

                var raised = model.Events.FirstOrDefault(candidate => string.Equals(candidate.Name, raiseStep.Event, StringComparison.OrdinalIgnoreCase))
                    ?? throw Failure($"The release has no event '{raiseStep.Event}'.", instance, input);
                occurrence = new NewEvent(
                    Guid.CreateVersion7(),
                    instance.ApplicationId,
                    raised.Name,
                    instance.ReleaseId,
                    raised.Entity.Id,
                    instance.SubjectId,
                    await PayloadAsync(context, model, entity, record, raiseStep, instance, input, cancellationToken),
                    instance.Id);
                state = ProcessStarts.Running;
                nextStep = following.Name;
                decision = null;
                output = new JsonObject { ["next"] = following.Name, ["eventId"] = occurrence.Id };
                details = new JsonObject { ["step"] = step.Name, ["next"] = following.Name };
                action = AuditActions.ProcessStepCompleted;
                break;
            case PackageOperationStepModel packageStep:
                if (!process.TryGetStep(packageStep.Next, out _))
                {
                    throw Failure($"The process '{process.Name}' has no step '{packageStep.Next}'.", instance, input);
                }

                // The compiled step does not say which inputs are required, so the manifest does.
                var packageOperation = packages.Find(packageStep.Package, packageStep.Version)?.Manifest.Operations
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, packageStep.Operation, StringComparison.OrdinalIgnoreCase))
                    ?? throw Failure(
                        $"The package '{packageStep.Package}' {packageStep.Version} has no operation '{packageStep.Operation}' installed.",
                        instance,
                        input);
                var failedPackageCallId = await PackageCalls.FindFailedAsync(context.Transaction, instance.Id, step.Name, cancellationToken);
                restagesPackageCall = failedPackageCallId is not null;
                packageCall = new NewPackageCall(
                    failedPackageCallId ?? Guid.CreateVersion7(),
                    instance.Id,
                    step.Name,
                    instance.ReleaseId,
                    packageStep.Package,
                    packageStep.Version,
                    packageOperation.Name,
                    packageStep.Retry,
                    packageStep.Idempotent,
                    await StageAsync(
                        context,
                        model,
                        entity,
                        record,
                        packageStep,
                        packageStep.Input,
                        name => packageOperation.Input.Any(field => field.Required && string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)),
                        instance,
                        input,
                        cancellationToken));
                state = ProcessStarts.Waiting;
                nextStep = step.Name;
                decision = null;
                output = new JsonObject { ["state"] = ProcessStarts.Waiting, ["operationId"] = packageCall.Id };
                details = new JsonObject
                {
                    ["step"] = step.Name,
                    ["package"] = packageStep.Package,
                    ["version"] = packageStep.Version,
                    ["operation"] = packageOperation.Name,
                    ["operationId"] = packageCall.Id,
                };
                action = AuditActions.OperationStaged;
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
        Record? updated = null;
        if (assignments is not null)
        {
            var result = await RecordCommands.SetAsync(context.Connection, model, entity, instance.ReleaseId, stored, assignments, instance.Now, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                throw Failure($"The record update was rejected: {Describe(result)}", instance, input);
            }

            await ComputedBackfill.MarkStaleWriteAsync(context.Connection, context.Transaction, context.Item.TenantId, model, entity, instance.ReleaseId, cancellationToken);
            updated = written;
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
            if (restagesCall)
            {
                await ConnectorCalls.RestageAsync(context.Transaction, call, cancellationToken);
            }
            else
            {
                await ConnectorCalls.InsertAsync(context.Transaction, call, cancellationToken);
            }
        }

        if (packageCall is not null)
        {
            if (restagesPackageCall)
            {
                await PackageCalls.RestageAsync(context.Transaction, packageCall, cancellationToken);
            }
            else
            {
                await PackageCalls.InsertAsync(context.Transaction, packageCall, cancellationToken);
            }
        }

        if (occurrence is not null)
        {
            await ProcessEvents.InsertAsync(context.Transaction, occurrence, cancellationToken);
            await StartSyncTriggersAsync(context, occurrence, cancellationToken);

            // The occurrence is delivered even when no async trigger listens now, because the
            // triggers come from the release that is active at delivery.
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.EventDeliverWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                eventId: occurrence.Id);
        }

        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(instance.Id, step.Name, instance.Revision, input, output, decision, null, instance.Now),
            cancellationToken);
        if (recordAudit is not null)
        {
            await AuditRecords.AppendAsync(context.Transaction, recordAudit, cancellationToken);
        }

        if (updated is not null && active is { } activeNow)
        {
            await DataChangeTriggerStarts.StartAfterUpdateAsync(
                context.Transaction,
                context.Item.TenantId,
                activeNow.Model,
                activeNow.ReleaseId,
                instance.ReleaseId,
                entity,
                stored,
                updated,
                [],
                logger,
                cancellationToken);
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

        if (packageCall is not null)
        {
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.PackageCallWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                processInstanceId: instance.Id,
                packageCallId: packageCall.Id);
        }
    }

    /// <summary>
    /// Starts the process of each <c>sync</c> trigger on the occurrence's event in the active release,
    /// in model order, in the step's transaction. A skipped start is logged and lets the step commit.
    /// Any other error throws, so the step rolls back with its event.
    /// </summary>
    private async Task StartSyncTriggersAsync(WorkItemContext context, NewEvent occurrence, CancellationToken cancellationToken)
    {
        var found = await EventTriggerStarts.FindAsync(
            context, models, occurrence.ApplicationId, occurrence.Event, TriggerMode.Sync, cancellationToken);
        if (found is not { Event: { } @event, Triggers: { Count: > 0 } triggers })
        {
            return;
        }

        var stored = new StoredEvent(occurrence.Id, occurrence.ApplicationId, occurrence.Event, occurrence.EntityId, occurrence.SubjectId, occurrence.Payload);
        foreach (var trigger in triggers)
        {
            var (_, skipped) = await EventTriggerStarts.StartAsync(
                context.Transaction, context.Item.TenantId, found.Model, found.ReleaseId, @event, stored, trigger, cancellationToken);
            if (skipped is not null)
            {
                LogSyncSkipped(trigger.Name, trigger.Process.Name, occurrence.SubjectId, occurrence.Id, skipped);
            }
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
                throw ExpressionFailure($"when of branch {index}", step, error, instance, input);
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
                throw ExpressionFailure($"value of the field '{field.Name}'", step, error, instance, input);
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
            throw ExpressionFailure("assignee of the task step", step, error, instance, input);
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
    /// Evaluates each input of a <c>callConnector</c> or package operation step, in the step's
    /// order, into the body the call stores. <paramref name="isRequired"/> says whether an input of
    /// the operation is required. Dates and date-times take the record API's text forms, and an optional input
    /// that gives null is stored as null. An input that fails at run time, or a required input that
    /// gives null, fails the step.
    /// </summary>
    private static async Task<JsonObject> StageAsync(
        WorkItemContext context,
        ApplicationModel model,
        EntityModel entity,
        Record record,
        ProcessStepModel step,
        IReadOnlyList<ConnectorInputModel> inputs,
        Func<string, bool> isRequired,
        LoadedInstance instance,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        foreach (var mapping in inputs)
        {
            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, mapping.Value, instance.Now, cancellationToken);
            if (result.Error is { } error)
            {
                throw ExpressionFailure($"input '{mapping.Input}'", step, error, instance, input);
            }

            if (result.Value is null && isRequired(mapping.Input))
            {
                throw Failure($"The required input '{mapping.Input}' of the step '{step.Name}' gave no value.", instance, input);
            }

            body[mapping.Input] = ToJson(result.Value, $"input '{mapping.Input}'", step, instance, input);
        }

        return body;
    }

    /// <summary>
    /// Evaluates each payload field of a <c>raiseEvent</c> step, in the event's declaration order,
    /// into the payload the occurrence stores. Dates and date-times take the record API's text
    /// forms, and a field that gives null is stored as null. A field that fails at run time fails
    /// the step.
    /// </summary>
    private static async Task<JsonObject> PayloadAsync(
        WorkItemContext context,
        ApplicationModel model,
        EntityModel entity,
        Record record,
        RaiseEventStepModel step,
        LoadedInstance instance,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject();
        foreach (var field in step.Fields)
        {
            var result = await RecordExpressions.EvaluateAsync(context.Connection, model, entity, record, field.Value, instance.Now, cancellationToken);
            if (result.Error is { } error)
            {
                throw ExpressionFailure($"payload field '{field.Field}'", step, error, instance, input);
            }

            payload[field.Field] = ToJson(result.Value, $"field '{field.Field}'", step, instance, input);
        }

        return payload;
    }

    /// <summary>
    /// A value an expression gave, in the record API's JSON form. <paramref name="name"/> names
    /// what gave it, such as <c>input 'amount'</c>, for the error of a value with no JSON form.
    /// </summary>
    private static JsonValue? ToJson(object? value, string name, ProcessStepModel step, LoadedInstance instance, JsonObject input) =>
        value switch
        {
            null => null,
            string text => JsonValue.Create(text),
            long number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            bool flag => JsonValue.Create(flag),
            DateOnly date => JsonValue.Create(date.ToString(DateFormat, CultureInfo.InvariantCulture)),
            DateTimeOffset time => JsonValue.Create(time.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture)),
            Guid id => JsonValue.Create(id.ToString("D")),
            var other => throw Failure($"The {name} of the step '{step.Name}' gave a value of type '{other.GetType().Name}'.", instance, input),
        };

    /// <summary>Each error of a rejected record write as its pointer and messages, such as <c>/values/amount: request.amountNegative</c>, or the outcome when it has none.</summary>
    internal static string Describe(RecordWriteResult result) =>
        result.Errors is { Count: > 0 } errors
            ? string.Join(", ", errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}"))
            : result.Outcome.ToString();

    private static ProcessStepException ExpressionFailure(string part, ProcessStepModel step, ExpressionRuntimeError error, LoadedInstance instance, JsonObject input) =>
        Failure($"The {part} of the step '{step.Name}' failed: {error.Kind}.", instance, input);

    private static string TypeName(Exception exception) => exception.GetType().FullName ?? exception.GetType().Name;

    private static ProcessStepException Failure(string message, LoadedInstance instance, JsonObject input, Exception? innerException = null) =>
        new(message, instance.Step, instance.Revision, instance.Now, input, innerException);

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId} of event {EventId}: {Reason}.")]
    private partial void LogSyncSkipped(string trigger, string process, Guid subjectId, Guid eventId, string reason);

    [LoggerMessage(LogLevel.Warning, "Step work item {ItemId} names no process instance, so it was discarded.")]
    private partial void LogNoInstance(Guid itemId);

    [LoggerMessage(LogLevel.Warning, "Process instance {InstanceId} is missing or not running (state {State}), so its step work item was discarded.")]
    private partial void LogNotRunning(Guid instanceId, string? state);

    [LoggerMessage(LogLevel.Warning, "Step {Step} of process instance {InstanceId} ran at revision {Revision}, but the instance changed since, so the step wrote nothing.")]
    private partial void LogStaleRevision(Guid instanceId, string step, long revision);
}
