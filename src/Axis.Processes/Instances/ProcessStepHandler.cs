using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Data.Profiles;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Typing;
using Axis.Processes.Work;
using Axis.Tenancy;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs the current step of a process instance on the release the instance is pinned to. The step
/// commits in the work item's transaction: an operation's record write, a task step's open human
/// task, a <c>callConnector</c> step's staged call, a <c>raiseEvent</c> step's event occurrence,
/// the instances its <c>sync</c> event triggers start and the <c>event.deliver</c> item that
/// delivers it to its <c>async</c> triggers, a <c>notify</c> step's in-app notification rows and its staged emails with their
/// <c>notification.email</c> items, the new instance state and revision, a step
/// history row, its <c>system</c> audit records and the next work item. A task step leaves the
/// instance <c>waiting</c>, with no next work item. A <c>callConnector</c> step leaves it
/// <c>waiting</c> too, and queues the <c>connector.call</c> item of its call, so no request is sent
/// inside the transaction. A package operation step does the same with a <c>package.call</c> item,
/// so no operation runs inside the transaction. A <c>timer</c> step leaves the instance <c>waiting</c>
/// too and queues a <c>process.timer</c> item due at the time the timer waits for, with the revision the
/// step left, and writes no audit record while it waits. An <c>updateRecord</c> step starts the data change
/// triggers of the update after its <c>record.updated</c> audit. A <c>createRecord</c> step creates
/// a record of another entity as <c>system</c>, through the rules of a record create, and starts the
/// data change triggers of the create after its <c>record.created</c> audit. A <c>sync</c> trigger and a data
/// change trigger come from the application's active release, and
/// its start runs as <c>system</c>, pinned to that release. A skipped <c>sync</c> start is logged
/// and lets the step commit. A step that finds the instance at another revision than it loaded
/// writes nothing. An operation step, a <c>createRecord</c> step, a <c>raiseEvent</c> step and a
/// <c>subProcess</c> step take the application's start lock shared before they read an entity table,
/// since they can start instances.
/// A <c>subProcess</c> step starts its child as <c>system</c> on the release the parent is pinned to,
/// after the parent's move to <c>waiting</c>, and writes no audit record of its own: the child's
/// <c>process.started</c> record names the parent. The end step of a child queues the
/// <c>process.resume</c> item that moves the parent on. A step that throws is rolled back, and the
/// failure callback then records the error in the history and marks the instance <c>failed</c>.
/// </summary>
internal sealed partial class ProcessStepHandler(ReleaseModelCache models, PackageCatalog packages, ITenantCatalog? tenants, ILogger<ProcessStepHandler> logger) : IWorkItemHandler
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

        await InstanceMetrics.RecordEndedAsync(models, context, instance, ProcessStarts.Failed, cancellationToken);
        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(instanceId, step, revision, failure?.Input ?? new JsonObject(), null, null, failure?.Message ?? TypeName(exception), failure?.StartedAt ?? instance.Now),
            cancellationToken);

        // The same fixed text the history row stores. It holds names and the error kind, never a value.
        LogStepFailed(instanceId, step, failure?.Message ?? TypeName(exception));
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
        if (!process.TryGetStep(instance.Step, out var step))
        {
            throw Failure($"The process '{process.Name}' has no step '{instance.Step}'.", instance, input);
        }

        // With a subject, the step's expressions read the record. Without one, they read the stored
        // parameter values, and no record is locked, read or written.
        EntityModel? entity = null;
        Record? stored = null;
        Record? record = null;
        (Guid ReleaseId, ApplicationModel Model)? active = null;
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate;
        if (instance.SubjectId is { } subjectId && instance.SubjectEntityId is { } entityId)
        {
            var subjectEntity = model.FindEntity(entityId)
                ?? throw Failure($"The release has no entity '{entityId}'.", instance, input);
            entity = subjectEntity;

            if (step is OperationStepModel or RaiseEventStepModel or CreateRecordStepModel or SubProcessStepModel)
            {
                // These steps can start an instance: an operation or a record create through a data change
                // trigger, a raised event through a sync trigger and a sub-process step its child. The start lock comes before any entity table is read, so a
                // migrating activation cannot deadlock with the step, and the active release read later is
                // read under it. The step keeps the release the instance is pinned to.
                await StartLock.TakeSharedAsync(context.Transaction, instance.ApplicationId, cancellationToken);
            }

            if (step is OperationStepModel)
            {
                // The lock makes the read below see the latest version, and a concurrent write of the
                // record waits until this step commits or rolls back.
                await RecordCommands.LockAsync(context.Connection, subjectEntity, subjectId, cancellationToken);
            }

            stored = await RecordQueries.GetAsync(context.Connection, model, subjectEntity, subjectId, cancellationToken)
                ?? throw Failure($"No record of '{subjectEntity.Name}' has the id '{subjectId}'.", instance, input);
            input["subjectVersion"] = stored.Version;

            // The step's expressions read the record with its pending computed fields computed by the active release,
            // whatever release the instance is pinned to. The write of an updateRecord step starts from the stored record.
            active = await models.FindActiveAsync(context, instance.ApplicationId, cancellationToken);
            record = active is { } release
                ? await PendingComputedReads.ApplyAsync(context.Connection, context.Transaction, release.Model, subjectEntity, stored, cancellationToken, model)
                : stored;
            var subject = record;
            evaluate = expression => RecordExpressions.EvaluateAsync(context.Connection, model, subjectEntity, subject, expression, instance.Now, cancellationToken);
        }
        else
        {
            if (step is SubProcessStepModel)
            {
                // The step starts a child, so the start lock comes before anything else.
                await StartLock.TakeSharedAsync(context.Transaction, instance.ApplicationId, cancellationToken);
            }

            // The compiler rejects these steps in a process without a subject (AXC0246), so this only
            // guards against a broken release. It runs before any lock or record read.
            if (step is OperationStepModel or RaiseEventStepModel or TaskStepModel)
            {
                throw Failure($"The step '{step.Name}' needs a subject.", instance, input);
            }

            if (step is CreateRecordStepModel)
            {
                // A record create can start an instance through a data change trigger, so it takes the
                // start lock before it touches an entity table, and reads the active release under it.
                await StartLock.TakeSharedAsync(context.Transaction, instance.ApplicationId, cancellationToken);
                active = await models.FindActiveAsync(context, instance.ApplicationId, cancellationToken);
            }

            var values = ProcessParameters.Values(process, instance.Parameters);
            evaluate = expression => Task.FromResult(ProcessParameters.Evaluate(expression, values, instance.Now, model.TimeZone));
        }

        string state;
        string nextStep;
        string? decision;
        JsonObject output;
        JsonObject details;
        string? action;
        DateTimeOffset? timerDueAt = null;
        PendingChild? child = null;
        List<KeyValuePair<FieldModel, object?>>? assignments = null;
        NewTask? task = null;
        TaskDeadlineModel? taskDeadline = null;
        IReadOnlyList<TaskEscalationModel> taskEscalations = [];
        NewConnectorCall? call = null;
        NewPackageCall? packageCall = null;
        var restagesCall = false;
        var restagesPackageCall = false;
        NewEvent? occurrence = null;
        NewNotification? notification = null;
        EntityModel? created = null;
        List<KeyValuePair<FieldModel, object?>>? creation = null;

        // An updateRecord, raiseEvent or task step ran only with a subject, so its entity and record are set below.
        switch (step)
        {
            case DecisionStepModel decisionStep:
                (decision, var target) = await DecideAsync(evaluate, decisionStep, instance, input);
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

                assignments = await EvaluateAsync(evaluate, operationStep.Set, entity!, operationStep, instance, input);
                state = ProcessStarts.Running;
                nextStep = after.Name;
                decision = null;
                output = new JsonObject { ["next"] = after.Name };
                details = new JsonObject { ["step"] = step.Name, ["next"] = after.Name };
                action = AuditActions.ProcessStepCompleted;
                break;
            case CreateRecordStepModel createStep:
                if (!process.TryGetStep(createStep.Next, out var afterCreate))
                {
                    throw Failure($"The process '{process.Name}' has no step '{createStep.Next}'.", instance, input);
                }

                created = model.FindEntity(createStep.Entity.Id)
                    ?? throw Failure($"The release has no entity '{createStep.Entity.Id}'.", instance, input);
                creation = await EvaluateAsync(evaluate, createStep.Set, created, createStep, instance, input);
                state = ProcessStarts.Running;
                nextStep = afterCreate.Name;
                decision = null;
                output = new JsonObject { ["next"] = afterCreate.Name };
                details = new JsonObject { ["step"] = step.Name, ["next"] = afterCreate.Name };
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
                var (assigneeKind, assignee) = await AssignAsync(evaluate, taskStep, instance, input);
                task = new NewTask(
                    Guid.CreateVersion7(),
                    instance.Id,
                    instance.ProcessId,
                    instance.ReleaseId,
                    instance.ApplicationId,
                    step.Name,
                    instance.SubjectEntityId!.Value,
                    instance.SubjectId!.Value,
                    assigneeKind,
                    assignee,
                    taskStep.Form.Id,
                    taskStep.DueIn is { } dueIn ? instance.Now + dueIn : null);
                taskDeadline = taskStep.Deadline;
                taskEscalations = taskStep.Escalations;
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
                    instance.Revision,
                    instance.ReleaseId,
                    connector.Name,
                    operation.Name,
                    await StageAsync(
                        evaluate,
                        callStep,
                        callStep.Input,
                        name => operation.Input.Any(field => field.Required && string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)),
                        instance,
                        input));
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
                    instance.SubjectId!.Value,
                    await PayloadAsync(evaluate, raiseStep, instance, input),
                    instance.Id);
                state = ProcessStarts.Running;
                nextStep = following.Name;
                decision = null;
                output = new JsonObject { ["next"] = following.Name, ["eventId"] = occurrence.Id };
                details = new JsonObject { ["step"] = step.Name, ["next"] = following.Name };
                action = AuditActions.ProcessStepCompleted;
                break;
            case NotifyStepModel notifyStep:
                if (!process.TryGetStep(notifyStep.Next, out var afterNotify))
                {
                    throw Failure($"The process '{process.Name}' has no step '{notifyStep.Next}'.", instance, input);
                }

                notification = await ProcessNotifications.EvaluateAsync(
                    notifyStep,
                    evaluate,
                    instance.ApplicationId,
                    instance.ReleaseId,
                    (part, error) => ExpressionFailure(part, notifyStep, error, instance, input),
                    (value, name) => ToJson(value, name, notifyStep, instance, input));
                state = ProcessStarts.Running;
                nextStep = afterNotify.Name;
                decision = null;
                output = new JsonObject { ["next"] = afterNotify.Name, ["notificationId"] = notification.Id };
                details = new JsonObject { ["step"] = step.Name, ["next"] = afterNotify.Name };
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
                    instance.Revision,
                    instance.ReleaseId,
                    packageStep.Package,
                    packageStep.Version,
                    packageOperation.Name,
                    packageStep.Retry,
                    packageStep.Idempotent,
                    await StageAsync(
                        evaluate,
                        packageStep,
                        packageStep.Input,
                        name => packageOperation.Input.Any(field => field.Required && string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)),
                        instance,
                        input));
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
            case TimerStepModel timerStep:
                if (!process.TryGetStep(timerStep.Next, out _))
                {
                    throw Failure($"The process '{process.Name}' has no step '{timerStep.Next}'.", instance, input);
                }

                var due = timerStep.Wait is { } wait
                    ? instance.Now + wait
                    : await UntilAsync(evaluate, timerStep, instance, input);
                timerDueAt = due;
                state = ProcessStarts.Waiting;
                nextStep = step.Name;
                decision = null;
                output = new JsonObject
                {
                    ["state"] = ProcessStarts.Waiting,
                    ["dueAt"] = due.UtcDateTime.ToString(ProcessNotifications.DateTimeFormat, CultureInfo.InvariantCulture),
                };

                // Waiting for a time is not an audit action. The resume writes the step's completion.
                details = new JsonObject();
                action = null;
                break;
            case SubProcessStepModel subStep:
                if (!process.TryGetStep(subStep.Next, out _))
                {
                    throw Failure($"The process '{process.Name}' has no step '{subStep.Next}'.", instance, input);
                }

                child = await PrepareChildAsync(context, model, subStep, instance, record, evaluate, input, cancellationToken);
                state = ProcessStarts.Waiting;
                nextStep = step.Name;
                decision = null;
                output = new JsonObject { ["state"] = ProcessStarts.Waiting, ["childInstanceId"] = child.Id };
                details = new JsonObject { ["step"] = step.Name, ["childInstanceId"] = child.Id };

                // No audit record of its own: the child's process.started record names this instance.
                action = null;
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

        if (state == ProcessStarts.Completed)
        {
            AxisMetrics.RecordInstanceEnded(context.Item.TenantId, model.Manifest.Name, process.Name, ProcessStarts.Completed);
        }

        // The child starts only once the instance is known to be current, so a stale step starts no
        // stray child. A start that cannot happen throws, which rolls back the move to waiting too.
        if (child is not null)
        {
            await StartChildAsync(context, model, instance, step.Name, child, input, cancellationToken);
        }

        // The record is written only once the instance is known to be current, so a stale step
        // writes nothing. A rejected write throws, which rolls back the instance's move too.
        AuditEntry? recordAudit = null;
        Record? updated = null;
        if (assignments is not null)
        {
            var result = await RecordCommands.SetAsync(context.Connection, model, entity!, instance.ReleaseId, stored!, assignments, instance.Now, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                throw Failure($"The record update was rejected: {Describe(result)}", instance, input);
            }

            await ComputedBackfill.MarkStaleWriteAsync(context.Connection, context.Transaction, context.Item.TenantId, model, entity!, instance.ReleaseId, cancellationToken);
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

        Record? createdRecord = null;
        if (creation is not null)
        {
            var result = await RecordCommands.CreateFromAsync(
                context.Connection, model, created!, instance.ReleaseId, creation, AuditActors.System, instance.Now, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                throw Failure($"The record create was rejected: {Describe(result)}", instance, input);
            }

            await ComputedBackfill.MarkStaleWriteAsync(context.Connection, context.Transaction, context.Item.TenantId, model, created!, instance.ReleaseId, cancellationToken);
            createdRecord = written;
            output["recordId"] = written.Id;
            recordAudit = new AuditEntry(
                AuditActors.System,
                AuditActions.RecordCreated,
                instance.ApplicationId,
                created!.Id,
                written.Id,
                instance.Id,
                new JsonObject { ["version"] = written.Version });
        }

        if (task is not null)
        {
            await ProcessTasks.InsertAsync(context.Transaction, task, cancellationToken);

            // Escalations count from the step's time too. They are queued by their time, before the
            // deadline, so a scenario that runs items in queue order passes them in the order of time.
            foreach (var (escalation, index) in taskEscalations
                .Select((escalation, index) => (escalation, index))
                .OrderBy(pair => pair.escalation.After))
            {
                await WorkItemQueue.EnqueueAsync(
                    context.Connection,
                    context.Transaction,
                    context.Item.TenantId,
                    ProcessStarts.TaskEscalateWorkItemKind,
                    instance.Now + escalation.After,
                    cancellationToken,
                    processInstanceId: instance.Id,
                    taskId: task.Id,
                    escalation: index);
            }

            if (taskDeadline is not null)
            {
                // The deadline counts from the step's time, as the due date does, so a scenario's clock decides it.
                await WorkItemQueue.EnqueueAsync(
                    context.Connection,
                    context.Transaction,
                    context.Item.TenantId,
                    ProcessStarts.TaskDeadlineWorkItemKind,
                    instance.Now + taskDeadline.After,
                    cancellationToken,
                    processInstanceId: instance.Id,
                    taskId: task.Id);
            }
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

        // A notification with no recipient has no in-app row.
        if (notification is not null && step is NotifyStepModel { Channels: var channels } && ProcessNotifications.ShouldStore(notification, channels))
        {
            await ProcessNotifications.InsertAsync(context.Transaction, notification, cancellationToken);
        }

        if (notification is not null && step is NotifyStepModel emailStep && emailStep.Channels.Contains(ProcessNotifications.EmailChannel, StringComparer.Ordinal))
        {
            await StageEmailsAsync(context, instance, model, emailStep, notification, cancellationToken);
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
                entity!,
                stored!,
                updated,
                [],
                logger,
                cancellationToken);
        }

        if (createdRecord is not null && active is { } activeAfterCreate)
        {
            await DataChangeTriggerStarts.StartAfterCreateAsync(
                context.Transaction,
                context.Item.TenantId,
                activeAfterCreate.Model,
                activeAfterCreate.ReleaseId,
                instance.ReleaseId,
                created!,
                createdRecord,
                logger,
                cancellationToken);
        }

        if (action is not null)
        {
            await AuditRecords.AppendAsync(
                context.Transaction,
                new AuditEntry(AuditActors.System, action, instance.ApplicationId, instance.SubjectEntityId, instance.SubjectId, instance.Id, details),
                cancellationToken);
        }

        if (state == ProcessStarts.Completed && instance.ParentInstanceId is not null)
        {
            // The parent waits on this child, so its resume item is queued with the end.
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.ProcessResumeWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                processInstanceId: instance.Id);
        }

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

        if (timerDueAt is { } timerDue)
        {
            // The item resumes the instance only while it waits at the revision this step left.
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.TimerWorkItemKind,
                timerDue,
                cancellationToken,
                processInstanceId: instance.Id,
                revision: instance.Revision + 1);
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
    /// Works out what a <c>subProcess</c> step starts: the child process in the pinned release, the
    /// record it runs for and the parameter values of a child without a subject. A child with a subject
    /// runs for the record <c>subject</c> gives, or for the parent's own record when it is left out. A
    /// record other than the parent's is locked and read as a trigger start reads it, with its pending
    /// computed fields computed by the active release when the child has a start condition. A missing
    /// record fails the step with a fixed text that names no value.
    /// </summary>
    private async Task<PendingChild> PrepareChildAsync(
        WorkItemContext context,
        ApplicationModel model,
        SubProcessStepModel step,
        LoadedInstance instance,
        Record? parentRecord,
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        var childProcess = model.Processes.FirstOrDefault(candidate => candidate.Id == step.ProcessId)
            ?? throw Failure($"The release has no process '{step.Process}'.", instance, input);
        var childId = Guid.CreateVersion7();
        if (childProcess.Entity is not { } reference)
        {
            var parameters = new JsonObject();
            foreach (var parameter in step.Parameters)
            {
                var result = await evaluate(parameter.Value);
                if (result.Error is { } error)
                {
                    throw ExpressionFailure($"parameter '{parameter.Parameter}'", step, error, instance, input);
                }

                parameters[parameter.Parameter] = ToJson(result.Value, $"parameter '{parameter.Parameter}'", step, instance, input);
            }

            return new PendingChild(childId, childProcess, null, null, null, parameters);
        }

        var childEntity = model.FindEntity(reference.Id)
            ?? throw Failure($"The release has no entity '{reference.Name}'.", instance, input);
        if (step.Subject is null)
        {
            // The child runs for the parent's own record, which the step has already read.
            if (instance.SubjectId is not { } ownId || instance.SubjectEntityId != childEntity.Id || parentRecord is null)
            {
                throw Failure($"The step '{step.Name}' needs a subject.", instance, input);
            }

            return new PendingChild(childId, childProcess, childEntity, ownId, parentRecord, null);
        }

        var given = await evaluate(step.Subject);
        if (given.Error is { } subjectError)
        {
            throw ExpressionFailure("subject", step, subjectError, instance, input);
        }

        if (given.Value is not Guid givenId)
        {
            throw Failure($"The subject of the step '{step.Name}' gave no record of '{childEntity.Name}'.", instance, input);
        }

        var subjectId = givenId;
        if (instance.SubjectId == subjectId && instance.SubjectEntityId == childEntity.Id && parentRecord is not null)
        {
            return new PendingChild(childId, childProcess, childEntity, subjectId, parentRecord, null);
        }

        // The lock makes the read below and the start condition see the latest version, as for a trigger start.
        var noRecord = $"The step '{step.Name}' found no record of '{childEntity.Name}' to start the process '{childProcess.Name}' for.";
        if (!await RecordCommands.LockAsync(context.Connection, childEntity, subjectId, cancellationToken)
            || await RecordQueries.GetAsync(context.Connection, model, childEntity, subjectId, cancellationToken) is not { } stored)
        {
            throw Failure(noRecord, instance, input);
        }

        if (childProcess.StartCondition is not null
            && await models.FindActiveAsync(context, instance.ApplicationId, cancellationToken) is { } active)
        {
            stored = await PendingComputedReads.ApplyAsync(context.Connection, context.Transaction, active.Model, childEntity, stored, cancellationToken, model);
        }

        return new PendingChild(childId, childProcess, childEntity, subjectId, stored, null);
    }

    /// <summary>
    /// Starts the child of a <c>subProcess</c> step on the release the parent is pinned to. A child that
    /// already runs for the record, or whose start condition is not true, fails the step with a fixed
    /// text that names the step and the process, never a value.
    /// </summary>
    private static async Task StartChildAsync(
        WorkItemContext context,
        ApplicationModel model,
        LoadedInstance instance,
        string stepName,
        PendingChild child,
        JsonObject input,
        CancellationToken cancellationToken)
    {
        var started = await ProcessStarter.StartChildAsync(
            context.Connection,
            context.Transaction,
            context.Item.TenantId,
            model,
            instance.ReleaseId,
            child.Process,
            child.Entity,
            child.SubjectId,
            child.Subject,
            child.Parameters,
            child.Id,
            instance.Id,
            stepName,
            cancellationToken);
        if (started.Outcome == ProcessStartOutcome.AlreadyRunning)
        {
            throw Failure($"The step '{stepName}' cannot start the process '{child.Process.Name}': it already runs for the record.", instance, input);
        }

        if (started.Outcome == ProcessStartOutcome.ConditionFailed)
        {
            throw Failure($"The step '{stepName}' cannot start the process '{child.Process.Name}': its start condition is not true.", instance, input);
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
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        DecisionStepModel step,
        LoadedInstance instance,
        JsonObject input)
    {
        for (var index = 0; index < step.Branches.Count; index++)
        {
            var branch = step.Branches[index];
            var result = await evaluate(branch.When);
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
    /// Evaluates the <c>until</c> of a timer step. A time in the past is due at once. An <c>until</c>
    /// that fails at run time, or gives null, fails the step.
    /// </summary>
    private static async Task<DateTimeOffset> UntilAsync(
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        TimerStepModel step,
        LoadedInstance instance,
        JsonObject input)
    {
        var result = await evaluate(step.Until!);
        if (result.Error is { } error)
        {
            throw ExpressionFailure("until", step, error, instance, input);
        }

        return result.Value is DateTimeOffset time
            ? time
            : throw Failure($"The until of the timer step '{step.Name}' gave no time.", instance, input);
    }

    /// <summary>
    /// Evaluates the value of each field of <paramref name="target"/> that an <c>updateRecord</c> or
    /// <c>createRecord</c> operation sets, in the entity's declaration order. A value that fails at
    /// run time fails the step.
    /// </summary>
    private static async Task<List<KeyValuePair<FieldModel, object?>>> EvaluateAsync(
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        IReadOnlyList<RecordAssignmentModel> set,
        EntityModel target,
        ProcessStepModel step,
        LoadedInstance instance,
        JsonObject input)
    {
        var values = new List<KeyValuePair<FieldModel, object?>>();
        foreach (var assignment in set)
        {
            if (!target.TryGetField(assignment.Field, out var field))
            {
                throw Failure($"The entity '{target.Name}' has no field '{assignment.Field}'.", instance, input);
            }

            var result = await evaluate(assignment.Value);
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
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        TaskStepModel step,
        LoadedInstance instance,
        JsonObject input)
    {
        if (step.Assignee.User is not { } user)
        {
            return (ProcessTasks.RoleAssignee, step.Assignee.Role!);
        }

        var result = await evaluate(user);
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
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        ProcessStepModel step,
        IReadOnlyList<ConnectorInputModel> inputs,
        Func<string, bool> isRequired,
        LoadedInstance instance,
        JsonObject input)
    {
        var body = new JsonObject();
        foreach (var mapping in inputs)
        {
            var result = await evaluate(mapping.Value);
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
        Func<ExpressionModel, Task<ExpressionEvaluationResult>> evaluate,
        RaiseEventStepModel step,
        LoadedInstance instance,
        JsonObject input)
    {
        var payload = new JsonObject();
        foreach (var field in step.Fields)
        {
            var result = await evaluate(field.Value);
            if (result.Error is { } error)
            {
                throw ExpressionFailure($"payload field '{field.Field}'", step, error, instance, input);
            }

            payload[field.Field] = ToJson(result.Value, $"field '{field.Field}'", step, instance, input);
        }

        return payload;
    }

    /// <summary>
    /// Stages the emails of a <c>notify</c> step with the <c>email</c> channel: one <c>pending</c> outbox row for each
    /// address and one queued <c>notification.email</c> item for each row. The addresses are those of the <c>user</c>
    /// recipients' profiles, then those of the <c>email</c> recipients. An address is used once, ignoring letter case,
    /// with the locale of its first recipient. A <c>user</c> recipient with no profile or no email gets none, and is logged
    /// by user id, never by address. An <c>email</c> recipient gets the release's first locale.
    /// </summary>
    private async Task StageEmailsAsync(
        WorkItemContext context,
        LoadedInstance instance,
        ApplicationModel model,
        NotifyStepModel step,
        NewNotification notification,
        CancellationToken cancellationToken)
    {
        var userIds = notification.Recipients
            .Where(recipient => recipient.Kind == ProcessNotifications.UserRecipient)
            .Select(recipient => recipient.Recipient)
            .ToArray();
        var contacts = await UserProfiles.FindContactsAsync(context.Connection, context.Transaction, userIds, cancellationToken);
        var targets = new List<(string Address, string? Locale)>();
        foreach (var userId in userIds)
        {
            if (contacts.TryGetValue(userId, out var contact) && !string.IsNullOrWhiteSpace(contact.Email))
            {
                targets.Add((contact.Email, contact.Locale));
            }
            else
            {
                LogNoEmailAddress(step.Name, instance.Id, userId);
            }
        }

        targets.AddRange(notification.EmailAddresses.Select(address => (address, (string?)null)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        targets = [.. targets.Where(target => seen.Add(target.Address))];
        if (targets.Count == 0)
        {
            return;
        }

        var hosts = tenants?.Find(context.Item.TenantId)?.Hosts;
        var host = hosts is { Count: > 0 } ? hosts[0] : null;
        var link = ProcessEmails.PageLink(model, host, notification.OpenPage, notification.OpenParameters);
        var values = NotificationTexts.RawValues(notification.Values);
        var emails = new List<NewEmail>(targets.Count);
        foreach (var (address, locale) in targets)
        {
            var texts = NotificationTexts.ChooseTexts(model, locale);
            var body = NotificationTexts.Render(texts, step.Body.TextKey, values);
            var id = Guid.CreateVersion7();
            emails.Add(new NewEmail(
                id,
                instance.ApplicationId,
                instance.ReleaseId,
                notification.Id,
                step.Name,
                address,
                texts?.Locale,
                NotificationTexts.Render(texts, step.Subject.TextKey, values),
                link is null ? body : $"{body}\n\n{link}",
                ProcessEmails.MessageId(id, host),
                notification.Values.ToJsonString()));
        }

        await ProcessEmails.InsertAsync(context.Transaction, emails, cancellationToken);
        foreach (var email in emails)
        {
            await WorkItemQueue.EnqueueAsync(
                context.Connection,
                context.Transaction,
                context.Item.TenantId,
                ProcessStarts.NotificationEmailWorkItemKind,
                DateTimeOffset.UtcNow,
                cancellationToken,
                processInstanceId: instance.Id,
                emailId: email.Id);
        }
    }

    /// <summary>
    /// A value an expression gave, in the record API's JSON form. <paramref name="name"/> names
    /// what gave it, such as <c>input 'amount'</c>, for the error of a value with no JSON form.
    /// </summary>
    private static JsonValue? ToJson(object? value, string name, ProcessStepModel step, LoadedInstance instance, JsonObject input) =>
        ProcessNotifications.TryToJson(value, out var json)
            ? json
            : throw Failure($"The {name} of the step '{step.Name}' gave a value of type '{value!.GetType().Name}'.", instance, input);

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

    /// <summary>The child a <c>subProcess</c> step starts: its new instance id, its process, and its record or parameter values.</summary>
    private sealed record PendingChild(Guid Id, ProcessModel Process, EntityModel? Entity, Guid? SubjectId, Record? Subject, JsonObject? Parameters);

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId} of event {EventId}: {Reason}.")]
    private partial void LogSyncSkipped(string trigger, string process, Guid subjectId, Guid eventId, string reason);

    [LoggerMessage(LogLevel.Information, "Step {Step} of process instance {InstanceId} sends no email to user {UserId}, who has no profile or no email.")]
    private partial void LogNoEmailAddress(string step, Guid instanceId, string userId);

    [LoggerMessage(LogLevel.Warning, "Step work item {ItemId} names no process instance, so it was discarded.")]
    private partial void LogNoInstance(Guid itemId);

    [LoggerMessage(LogLevel.Warning, "Process instance {InstanceId} is missing or not running (state {State}), so its step work item was discarded.")]
    private partial void LogNotRunning(Guid instanceId, string? state);

    [LoggerMessage(LogLevel.Warning, "Step {Step} of process instance {InstanceId} failed: {Error}")]
    private partial void LogStepFailed(Guid instanceId, string step, string error);

    [LoggerMessage(LogLevel.Warning, "Step {Step} of process instance {InstanceId} ran at revision {Revision}, but the instance changed since, so the step wrote nothing.")]
    private partial void LogStaleRevision(Guid instanceId, string step, long revision);
}
