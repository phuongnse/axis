using System.Diagnostics;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>How a task completion ended.</summary>
public enum TaskCompletionOutcome
{
    /// <summary>The values are written, the task is closed and the instance resumes. The caller commits.</summary>
    Completed,

    /// <summary>The values are written and the task stays open. The caller commits.</summary>
    Saved,

    /// <summary>The task does not exist, or belongs to another application.</summary>
    NotFound,

    /// <summary>The task is assigned to someone else, or a role task is claimed by another holder.</summary>
    Denied,

    /// <summary>The task is not open.</summary>
    AlreadyCompleted,

    /// <summary>The body reader said the request is not JSON.</summary>
    UnsupportedContent,

    /// <summary>The body, a computed field or a validation is invalid, see <see cref="TaskCompletionResult.Errors"/>.</summary>
    Invalid,

    /// <summary>The body names a version other than the stored one.</summary>
    StaleVersion,

    /// <summary>The record write failed, see <see cref="TaskCompletionResult.Write"/>.</summary>
    WriteFailed,
}

/// <summary>
/// The result of a task completion or save. <see cref="Errors"/> are keyed by JSON Pointer. <see cref="Write"/> is set for
/// <see cref="TaskCompletionOutcome.WriteFailed"/>. <see cref="Version"/> is the subject record's new version after
/// <see cref="TaskCompletionOutcome.Saved"/>.
/// </summary>
public sealed record TaskCompletionResult(
    TaskCompletionOutcome Outcome,
    IDictionary<string, string[]>? Errors = null,
    RecordWriteResult? Write = null,
    long? Version = null);

/// <summary>
/// Completes a locked task in the caller's transaction, as the task API and the test scenario
/// runner do. It checks, in order, that the task exists in the application (not found), that the
/// actor may act on it (denied) and that it is open (already completed), then that the request is
/// JSON and its body is valid. A completion is refused as invalid when a field of the task's form
/// is visible, has a <c>requiredWhen</c> that holds and is <c>null</c> in the record as it will be
/// stored, whether or not the body sends values. It is also refused as invalid, with "Cannot be set.",
/// when the body sends a value for a field that the form hides or makes read-only through a condition
/// in the record as it will be stored. The body gives the <c>outcome</c> and optional <c>values</c> and
/// <c>version</c>, and a value is written only for a field the task's form makes editable and the
/// actor may read. One transaction then writes the values as a record update on the task's release,
/// closes the task, resumes the instance on the outcome's next step and appends the audit record
/// <c>task.completed</c>. A completion with values then starts the data change triggers of the
/// update, from the active release, in the same transaction. When the update makes a transition with
/// effects, the completion also audits each effect record as <c>record.created</c> for the actor and
/// starts the create triggers on its entity. A failing effect refuses the completion as
/// <see cref="TaskCompletionOutcome.WriteFailed"/> with <see cref="RecordWriteOutcome.EffectRefused"/>, and the task stays open. A task shows its form and step as the
/// release its instance is pinned to declares them, and a completion uses that release too.
/// The assignee check is the only gate: no entity grant is needed and no record filter applies.
/// Only the claimer may complete a claimed role task.
/// A role assignee counts only when the active release declares the role.
/// <see cref="SaveLockedAsync"/> saves a task's changes with the same checks and the same record write, and
/// leaves the task open. Its body has no <c>outcome</c> and needs <c>values</c> and <c>version</c>. The write
/// names no deciding task, and its accountable user is the actor. It appends the audit record
/// <c>record.updated</c> for the actor, as the record API does, with no process instance id, then starts the
/// data change triggers and the effects' create triggers as a completion does. It never touches the task,
/// its instance, its history or its work items.
/// The command never commits or rolls back. A result other than <see cref="TaskCompletionOutcome.Completed"/>
/// or <see cref="TaskCompletionOutcome.Saved"/> may have written partly, so the caller rolls back.
/// </summary>
public static class TaskCompletionCommand
{
    /// <param name="active">The active release's model, which decides the actor's roles and which fields the actor may read.</param>
    /// <param name="activeReleaseId">The active release's id, which the data change triggers are pinned to.</param>
    /// <param name="task">The task as <see cref="TaskQueries.LockAsync"/> returned it.</param>
    /// <param name="actor">The user completing the task. <see cref="RecordActor.Roles"/> are the roles the user holds in <paramref name="active"/>.</param>
    /// <param name="releaseOf">Gives the model of the release a task is pinned to.</param>
    /// <param name="readBody">Reads the request body, or returns null when the request is not JSON.</param>
    public static async Task<TaskCompletionResult> CompleteLockedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        ProcessTaskRow? task,
        RecordActor actor,
        Func<ProcessTaskRow, CancellationToken, Task<ApplicationModel>> releaseOf,
        RecordBodyReader readBody,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(releaseOf);
        ArgumentNullException.ThrowIfNull(readBody);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (task is null || task.ApplicationId != active.Manifest.Id)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.NotFound);
        }

        if (!TaskQueries.MayComplete(task, actor.Id, actor.Roles))
        {
            return new TaskCompletionResult(TaskCompletionOutcome.Denied);
        }

        if (task.State != ProcessTasks.Open)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.AlreadyCompleted);
        }

        if (await readBody(cancellationToken) is not { } utf8Body)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.UnsupportedContent);
        }

        var release = await releaseOf(task, cancellationToken);
        var (process, step) = StepOf(release, task);

        // Open through the task's resume, so the next work item stores this span as the one that queued it.
        using var activity = AxisTelemetry.Source.StartActivity(
            AxisTelemetry.ProcessStep,
            ActivityKind.Internal,
            default(ActivityContext),
            [
                new(AxisTelemetry.TenantTag, tenantId),
                new(AxisTelemetry.ApplicationTag, release.Manifest.Name),
                new(AxisTelemetry.ProcessTag, process.Name),
                new(AxisTelemetry.StepTag, task.Step),
            ]);
        var form = FormOf(release, task, step);
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        var readable = Readable(active, actor.Roles, task.SubjectEntityId);
        var body = TaskCompletionBody.Read(utf8Body, step, form, entity, release, readable);
        if (body.Errors is { } errors)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.Invalid, errors);
        }

        long? readVersion = null;
        long? writtenVersion = null;
        IReadOnlyList<string> fields = [];
        SubjectWrite? subject = null;
        JsonArray? changes = null;
        var write = new RecordWriteContext(actor.Id, task.Id);
        if (body.Input is { } input)
        {
            var (failure, written) = await WriteValuesAsync(
                connection, transaction, tenantId, release, entity, form, task, input, write, cipher, loggerFactory, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            subject = written!;
            changes = RunValues.Changes(release, entity, subject.Stored, subject.Written);
            readVersion = subject.Stored.Version;
            writtenVersion = subject.Written.Version;
            fields = subject.Fields;
        }
        else if (form.HasRequiredWhen)
        {
            // A completion that sends no values still needs the form's required fields, as stored.
            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            if (await RecordQueries.GetAsync(connection, release, entity, task.SubjectId, cipher, cancellationToken) is not { } stored)
            {
                return new TaskCompletionResult(
                    TaskCompletionOutcome.WriteFailed, Write: new RecordWriteResult(RecordWriteOutcome.NotFound));
            }

            var sets = await RecordSets.ReadAsync(connection, release, entity, stored, [], cancellationToken);
            if (RecordFormRequirements.Check(release, entity, form, stored, [], [], now, sets: sets) is { } missing)
            {
                return new TaskCompletionResult(TaskCompletionOutcome.Invalid, missing);
            }
        }

        var outcome = body.Outcome!;
        if (!await ProcessTasks.TryCompleteAsync(transaction, task.Id, outcome.Name, actor.Id, cancellationToken))
        {
            return new TaskCompletionResult(TaskCompletionOutcome.AlreadyCompleted);
        }

        // The task is closed, so its deadline has nothing left to expire.
        await ProcessTasks.DeletePendingItemsAsync(transaction, task.Id, cancellationToken);
        await ProcessTasks.ResumeAsync(
            transaction,
            task,
            new TaskCompletion(outcome.Name, outcome.Next, actor.Id, readVersion, writtenVersion, fields, changes),
            tenantId,
            cancellationToken);

        // The triggers run once the task is closed and its instance resumes, so a trigger on the
        // task's own process finds the instance running and is skipped.
        if (subject is not null && body.Input is { } saved)
        {
            await StartAfterWriteAsync(
                transaction, tenantId, active, activeReleaseId, task, entity, saved, subject, write, actor.Id, task.ProcessInstanceId, loggerFactory, cancellationToken);
        }

        return new TaskCompletionResult(TaskCompletionOutcome.Completed);
    }

    /// <summary>
    /// Saves the values of the locked <paramref name="task"/>'s form to its subject record in the caller's transaction and
    /// leaves the task open. The parameters are those of <see cref="CompleteLockedAsync"/>. The result is
    /// <see cref="TaskCompletionOutcome.Saved"/> with the record's new version, or a failure after which the caller rolls back.
    /// </summary>
    public static async Task<TaskCompletionResult> SaveLockedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        ProcessTaskRow? task,
        RecordActor actor,
        Func<ProcessTaskRow, CancellationToken, Task<ApplicationModel>> releaseOf,
        RecordBodyReader readBody,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(releaseOf);
        ArgumentNullException.ThrowIfNull(readBody);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (task is null || task.ApplicationId != active.Manifest.Id)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.NotFound);
        }

        if (!TaskQueries.MayComplete(task, actor.Id, actor.Roles))
        {
            return new TaskCompletionResult(TaskCompletionOutcome.Denied);
        }

        if (task.State != ProcessTasks.Open)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.AlreadyCompleted);
        }

        if (await readBody(cancellationToken) is not { } utf8Body)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.UnsupportedContent);
        }

        var release = await releaseOf(task, cancellationToken);
        var (_, step) = StepOf(release, task);
        var form = FormOf(release, task, step);
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        var readable = Readable(active, actor.Roles, task.SubjectEntityId);
        var body = TaskCompletionBody.ReadSave(utf8Body, form, entity, release, readable);
        if (body.Errors is { } errors)
        {
            return new TaskCompletionResult(TaskCompletionOutcome.Invalid, errors);
        }

        // The body reader accepts a save only with values, so there is an input to write.
        var input = body.Input!;
        var write = new RecordWriteContext(actor.Id, null);
        var (failure, written) = await WriteValuesAsync(
            connection, transaction, tenantId, release, entity, form, task, input, write, cipher, loggerFactory, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var subject = written!;
        await AuditRecords.AppendAsync(
            transaction,
            RecordCommand.RecordAudit(actor, AuditActions.RecordUpdated, release, entity, task.SubjectId, subject.Written.Version, subject.Fields),
            cancellationToken);
        await StartAfterWriteAsync(
            transaction, tenantId, active, activeReleaseId, task, entity, input, subject, write, actor.Id, null, loggerFactory, cancellationToken);
        return new TaskCompletionResult(TaskCompletionOutcome.Saved, Version: subject.Written.Version);
    }

    /// <summary>The subject record before and after a write, the effects' records and the names of the fields the body set.</summary>
    private sealed record SubjectWrite(Record Stored, Record Written, IReadOnlyList<EffectRecord> Effects, IReadOnlyList<string> Fields);

    // The one record write of a completion and a save: the values are written as a record update on the task's release.
    // The lock makes the read see the latest version, which the computed fields and validations use.
    private static async Task<(TaskCompletionResult? Failure, SubjectWrite? Written)> WriteValuesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel release,
        EntityModel entity,
        FormModel form,
        ProcessTaskRow task,
        RecordInput input,
        RecordWriteContext write,
        RecordCipher? cipher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        await RecordCommands.LockAsync(connection, entity, task.SubjectId, cancellationToken);
        if (await RecordQueries.GetAsync(connection, release, entity, task.SubjectId, cipher, cancellationToken) is not { } stored)
        {
            return (new TaskCompletionResult(
                TaskCompletionOutcome.WriteFailed, Write: new RecordWriteResult(RecordWriteOutcome.NotFound)), null);
        }

        var matched = await RecordMatcher.WithMatchesAsync(connection, release, entity, input.Values, stored, cancellationToken);
        var computed = RecordComputer.ComputeUpdate(release, entity, stored, matched, input.Rows);
        if (computed.Errors is { } computeErrors)
        {
            return (new TaskCompletionResult(TaskCompletionOutcome.Invalid, computeErrors), null);
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var sent = RecordCommand.SetFields(entity, input).ToList();
        var sets = entity.Validations.Count > 0 || form.HasRequiredWhen || (sent.Count > 0 && form.HasVisibleOrReadOnlyWhen)
            ? await RecordSets.ReadAsync(connection, release, entity, stored, input.Links, cancellationToken)
            : RecordSets.Empty;
        var failures = RecordFormRequirements.Merge(
            RecordFormRequirements.Check(release, entity, form, stored, computed.Values, computed.Rows, now, sent, sets),
            RecordValidator.ValidateUpdate(release, entity, stored, computed.Values, computed.Rows, now, sets));
        if (failures is not null)
        {
            return (new TaskCompletionResult(TaskCompletionOutcome.Invalid, failures), null);
        }

        var result = await RecordCommands.UpdateAsync(
            connection, release, entity, task.ReleaseId, write, task.SubjectId, input.Version!.Value, computed.Values, computed.Rows, input.Links, cipher: cipher, cancellationToken: cancellationToken);
        if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
        {
            if (result.Outcome == RecordWriteOutcome.StaleVersion)
            {
                return (new TaskCompletionResult(TaskCompletionOutcome.StaleVersion), null);
            }

            if (result.Outcome is RecordWriteOutcome.LifecycleRefused or RecordWriteOutcome.EffectRefused)
            {
                RecordLifecycleLog.Refused(loggerFactory, entity.Id, result.Point, task.SubjectId);
            }

            return (new TaskCompletionResult(TaskCompletionOutcome.WriteFailed, Write: result), null);
        }

        await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, tenantId, release, entity, task.ReleaseId, cancellationToken);
        return (null, new SubjectWrite(stored, written, result.Effects, sent));
    }

    // Starts the update triggers of the active release for the write, then audits the effects' records and starts their create
    // triggers. The effects' audit records link to processInstanceId, which a save leaves null as its own audit record does.
    private static async Task StartAfterWriteAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel active,
        Guid activeReleaseId,
        ProcessTaskRow task,
        EntityModel entity,
        RecordInput input,
        SubjectWrite subject,
        RecordWriteContext write,
        string actorId,
        Guid? processInstanceId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        await DataChangeTriggerStarts.StartAfterUpdateAsync(
            transaction,
            tenantId,
            active,
            activeReleaseId,
            task.ReleaseId,
            entity,
            subject.Stored,
            subject.Written,
            input.Rows.Select(rows => rows.Collection.Name).Concat(input.Links.Select(links => links.Field.Name)),
            write,
            loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
            cancellationToken);
        await EffectRecordWrites.AppendAsync(
            transaction,
            tenantId,
            active,
            activeReleaseId,
            task.ReleaseId,
            task.ApplicationId,
            processInstanceId,
            actorId,
            subject.Effects,
            write,
            loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
            cancellationToken);
    }

    /// <summary>The process and task step of <paramref name="task"/> in <paramref name="release"/>, the release the task is pinned to.</summary>
    /// <exception cref="InvalidOperationException">The task's process or step is not in the release.</exception>
    public static (ProcessModel Process, TaskStepModel Step) StepOf(ApplicationModel release, ProcessTaskRow task)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(task);

        // The worker created the task from this release, so its process and task step are in it.
        var process = release.Processes.FirstOrDefault(candidate => candidate.Id == task.ProcessId)
            ?? throw new InvalidOperationException($"The process of task '{task.Id}' is not in its release.");
        return process.TryGetStep(task.Step, out var found) && found is TaskStepModel step
            ? (process, step)
            : throw new InvalidOperationException($"The task step of task '{task.Id}' is not in its release.");
    }

    /// <summary>The form of <paramref name="step"/> in <paramref name="release"/>.</summary>
    public static FormModel FormOf(ApplicationModel release, ProcessTaskRow task, TaskStepModel step)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(step);

        return release.TryGetForm(step.Form.Name, out var form)
            ? form
            : throw new InvalidOperationException($"The form of task '{task.Id}' is not in its release.");
    }

    /// <summary>
    /// The declared names of the fields of the subject entity that <paramref name="roles"/> may
    /// read in the <paramref name="active"/> release, or null for every field. A user with no
    /// <c>read</c> grant on it, or a subject entity no longer in the release, reads no field.
    /// </summary>
    public static IReadOnlySet<string>? Readable(ApplicationModel active, IEnumerable<string> roles, Guid subjectEntityId)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(roles);

        return active.FindEntity(subjectEntityId) is { } entity
            ? PolicyEvaluator.Fields(active, roles, entity, PolicyAction.Read)
            : new HashSet<string>(StringComparer.Ordinal);
    }
}
