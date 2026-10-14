using Axis.Configuration.Model;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Presentation.Sites;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Npgsql;

namespace Axis.Server.Processes;

/// <summary>
/// The task API endpoints that list the current user's open human tasks, read one task and
/// complete one. The current user is the session user. A user may act on a task assigned to their
/// id, or to a role they hold. A request is checked in order:
/// signed-in user (401), application (404), then for a list the paging (400), and for a read or a
/// completion the task id and the task's application (404) and who may act (403).
/// A completion then locks the task and checks that it is open (409), then the content type (415),
/// the body (400) and the record write of its values (400, 409). One transaction writes the values,
/// closes the task, resumes the instance on the outcome's next step and appends the audit record
/// <c>task.completed</c>. A completion with values then starts the data change triggers of the
/// update, from the active release, in the same transaction. A completion that fails writes nothing. A task shows its process, step,
/// outcomes and form as the release its instance is pinned to declares them, and a completion
/// uses that release too. Its subject label is the record's display field in the active release.
/// The assignee check is the only gate: no entity grant is needed and no record filter applies.
/// The user's <c>read</c> grants on the subject entity in the active release decide which fields a
/// task shows. Its form and entity leave out the fields they do not cover, matched by name, and a
/// form section left with no field is dropped. The subject label is null unless they cover the
/// display field, and a completion refuses a value for a field they do not cover as one the form
/// does not make editable. The problem titles never contain text from the request.
/// </summary>
internal static class TaskEndpoints
{
    public static void MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tasks = endpoints.MapGroup("/api/apps/{app}/tasks");
        tasks.MapGet("", ListAsync);
        tasks.MapGet("/{id}", GetAsync);
        tasks.MapPost("/{id}/complete", CompleteAsync);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string app,
        string? page,
        string? pageSize,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return ApplicationNotFound();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        if (!PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size))
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await TaskQueries.ListOpenAsync(
            connection, active.Model.Manifest.Id, user.Id, user.Roles, pageNumber, size, cancellationToken);
        var labels = await ReadLabelsAsync(connection, active.Model, result.Items, cancellationToken);
        var readable = new Dictionary<Guid, IReadOnlySet<string>?>();
        var items = new List<TaskListItem>(result.Items.Count);
        foreach (var task in result.Items)
        {
            var (release, process, step) = await TaskStepAsync(resolver, task, cancellationToken);
            if (!readable.TryGetValue(task.SubjectEntityId, out var fields))
            {
                fields = Readable(active.Model, user.Roles, task.SubjectEntityId);
                readable[task.SubjectEntityId] = fields;
            }

            items.Add(new TaskListItem(
                task.Id,
                process.Name,
                step.Name,
                step.Label.TextKey,
                Subject(release, active.Model, task, labels, fields),
                task.DueAt?.UtcDateTime,
                task.CreatedAt.UtcDateTime));
        }

        return Results.Ok(new TaskListResponse(items, pageNumber, size, result.TotalCount));
    }

    private static async Task<IResult> GetAsync(
        string app,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return ApplicationNotFound();
        }

        if (!TryParseId(id, out var taskId))
        {
            return TaskNotFound();
        }

        // A task of another application does not exist under this one, so it is a 404 as well.
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await TaskQueries.FindAsync(connection, taskId, cancellationToken) is not { } task
            || task.ApplicationId != active.Model.Manifest.Id)
        {
            return TaskNotFound();
        }

        if (!TaskQueries.MayAct(task, user.Id, user.Roles))
        {
            return AssignedToSomeoneElse();
        }

        return Results.Ok(await DescribeAsync(resolver, connection, active.Model, user.Roles, task, cancellationToken));
    }

    // The body is read here rather than bound, so every body problem is reported by JSON Pointer,
    // and only after the task is locked and found open.
    private static async Task<IResult> CompleteAsync(
        string app,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return ApplicationNotFound();
        }

        if (!TryParseId(id, out var taskId))
        {
            return TaskNotFound();
        }

        var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            // The lock makes a concurrent completion of the task wait until this one commits or
            // rolls back, and then find the task as it was left.
            var task = await TaskQueries.LockAsync(transaction, taskId, cancellationToken);
            var failure = await CompleteLockedAsync(request, resolver, connection, transaction, active, task, user, tenantId, loggerFactory, cancellationToken);
            if (failure is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return failure;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var completed = await TaskQueries.FindAsync(connection, taskId, cancellationToken)
            ?? throw new InvalidOperationException($"The completed task '{taskId}' could not be read.");
        return Results.Ok(await DescribeAsync(resolver, connection, active.Model, user.Roles, completed, cancellationToken));
    }

    /// <summary>
    /// Completes the locked <paramref name="task"/> in <paramref name="transaction"/>, or returns
    /// the failure, after which the caller rolls every write back.
    /// </summary>
    private static async Task<IResult?> CompleteLockedAsync(
        HttpRequest request,
        ActiveApplicationResolver resolver,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActiveApplication current,
        ProcessTaskRow? task,
        CurrentUserResponse user,
        string tenantId,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var active = current.Model;
        if (task is null || task.ApplicationId != active.Manifest.Id)
        {
            return TaskNotFound();
        }

        if (!TaskQueries.MayAct(task, user.Id, user.Roles))
        {
            return AssignedToSomeoneElse();
        }

        if (task.State != ProcessTasks.Open)
        {
            return AlreadyCompleted();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (release, _, step) = await TaskStepAsync(resolver, task, cancellationToken);
        var form = FormOf(release, task, step);
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        var readable = Readable(active, user.Roles, task.SubjectEntityId);
        var body = TaskCompletionBody.Read(await RecordEndpoints.ReadBodyAsync(request, cancellationToken), step, form, entity, release, readable);
        if (body.Errors is { } errors)
        {
            return Results.ValidationProblem(errors);
        }

        long? readVersion = null;
        long? writtenVersion = null;
        IReadOnlyList<string> fields = [];
        Record? previous = null;
        Record? updated = null;
        if (body.Input is { } input)
        {
            // The values are written as a record update on the task's release. The lock makes the
            // read below see the latest version, which the computed fields and validations use.
            await RecordCommands.LockAsync(connection, entity, task.SubjectId, cancellationToken);
            if (await RecordQueries.GetAsync(connection, release, entity, task.SubjectId, cancellationToken) is not { } stored)
            {
                return RecordEndpoints.WriteFailure(new RecordWriteResult(RecordWriteOutcome.NotFound));
            }

            var computed = RecordComputer.ComputeUpdate(release, entity, stored, input.Values, input.Rows);
            if (computed.Errors is { } computeErrors)
            {
                return Results.ValidationProblem(computeErrors);
            }

            var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
            if (RecordValidator.ValidateUpdate(release, entity, stored, computed.Values, computed.Rows, now) is { } failures)
            {
                return Results.ValidationProblem(failures);
            }

            var result = await RecordCommands.UpdateAsync(
                connection, release, entity, task.SubjectId, input.Version!.Value, computed.Values, computed.Rows, cancellationToken);
            if (result is not { Outcome: RecordWriteOutcome.Written, Record: { } written })
            {
                return result.Outcome == RecordWriteOutcome.StaleVersion ? StaleVersion() : RecordEndpoints.WriteFailure(result);
            }

            previous = stored;
            updated = written;
            readVersion = stored.Version;
            writtenVersion = written.Version;
            fields = [.. RecordEndpoints.SetFields(entity, input)];
        }

        var outcome = body.Outcome!;
        if (!await ProcessTasks.TryCompleteAsync(transaction, task.Id, outcome.Name, user.Id, cancellationToken))
        {
            return AlreadyCompleted();
        }

        await ProcessTasks.ResumeAsync(
            transaction,
            task,
            new TaskCompletion(outcome.Name, outcome.Next, user.Id, user.DisplayName, readVersion, writtenVersion, fields),
            tenantId,
            cancellationToken);

        // The triggers run once the task is closed and its instance resumes, so a trigger on the
        // task's own process finds the instance running and is skipped.
        if (previous is not null && updated is not null && body.Input is { } saved)
        {
            await DataChangeTriggerStarts.StartAfterUpdateAsync(
                transaction,
                tenantId,
                active,
                current.ReleaseId,
                task.ReleaseId,
                entity,
                previous,
                updated,
                saved.Rows.Select(rows => rows.Collection.Name),
                loggerFactory.CreateLogger(typeof(DataChangeTriggerStarts)),
                cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// The task as a single read returns it, with its form as its release declares it, less the
    /// fields <paramref name="roles"/> may not read.
    /// </summary>
    private static async Task<TaskResponse> DescribeAsync(
        ActiveApplicationResolver resolver,
        NpgsqlConnection connection,
        ApplicationModel active,
        IReadOnlyList<string> roles,
        ProcessTaskRow task,
        CancellationToken cancellationToken)
    {
        var labels = await ReadLabelsAsync(connection, active, [task], cancellationToken);
        var (release, process, step) = await TaskStepAsync(resolver, task, cancellationToken);
        var formModel = FormOf(release, task, step);
        var form = ApplicationSites.DescribeForm(release, formModel.Name);
        var entity = ApplicationSites.DescribeEntity(release, formModel.Entity.Name);
        var readable = Readable(active, roles, task.SubjectEntityId);
        var sections = form.Sections;
        if (readable is not null)
        {
            sections = [.. sections
                .Select(section => section with { Fields = [.. section.Fields.Where(field => readable.Contains(field.Name))] })
                .Where(section => section.Fields.Count > 0)];
            entity = entity with { Fields = [.. entity.Fields.Where(field => readable.Contains(field.Name))] };
        }

        return new TaskResponse(
            task.Id,
            process.Name,
            task.ProcessInstanceId,
            step.Name,
            step.Label.TextKey,
            task.State,
            new TaskAssignee(
                task.AssigneeKind == ProcessTasks.UserAssignee ? task.Assignee : null,
                task.AssigneeKind == ProcessTasks.RoleAssignee ? task.Assignee : null),
            Subject(release, active, task, labels, readable),
            new TaskForm(form.Name, sections, entity),
            [.. step.Outcomes.Select(outcome => new TaskOutcome(outcome.Name, outcome.Label.TextKey))],
            task.DueAt?.UtcDateTime,
            task.CreatedAt.UtcDateTime,
            task.CompletedAt?.UtcDateTime,
            task.CompletedBy,
            task.Outcome);
    }

    private static FormModel FormOf(ApplicationModel release, ProcessTaskRow task, TaskStepModel step) =>
        release.TryGetForm(step.Form.Name, out var form)
            ? form
            : throw new InvalidOperationException($"The form of task '{task.Id}' is not in its release.");

    /// <summary>The model of the task's release, with the task's process and step as that release declares them.</summary>
    private static async Task<(ApplicationModel Release, ProcessModel Process, TaskStepModel Step)> TaskStepAsync(
        ActiveApplicationResolver resolver, ProcessTaskRow task, CancellationToken cancellationToken)
    {
        // The worker created the task from this release, so its process and task step are in it.
        var release = await resolver.GetReleaseModelAsync(task.ReleaseId, cancellationToken);
        var process = release.Processes.FirstOrDefault(candidate => candidate.Id == task.ProcessId)
            ?? throw new InvalidOperationException($"The process of task '{task.Id}' is not in its release.");
        return process.TryGetStep(task.Step, out var found) && found is TaskStepModel step
            ? (release, process, step)
            : throw new InvalidOperationException($"The task step of task '{task.Id}' is not in its release.");
    }

    /// <summary>
    /// The subject labels of <paramref name="tasks"/>, by subject record id, read through the
    /// display field of the active release with one query per subject entity. A subject whose
    /// entity or display field is not in the active release has no label.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, string>> ReadLabelsAsync(
        NpgsqlConnection connection,
        ApplicationModel active,
        IReadOnlyList<ProcessTaskRow> tasks,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<Guid, string>();
        foreach (var group in tasks.GroupBy(task => task.SubjectEntityId))
        {
            if (active.FindEntity(group.Key) is not { } entity)
            {
                continue;
            }

            var ids = group.Select(task => task.SubjectId).Distinct().ToList();
            foreach (var (subjectId, label) in await RecordQueries.ReadDisplayLabelsAsync(connection, entity, ids, cancellationToken))
            {
                labels[subjectId] = label;
            }
        }

        return labels;
    }

    /// <summary>
    /// The subject of <paramref name="task"/>, with its label only when <paramref name="readable"/>
    /// covers the display field of the subject entity in the <paramref name="active"/> release.
    /// </summary>
    private static TaskSubject Subject(
        ApplicationModel release,
        ApplicationModel active,
        ProcessTaskRow task,
        IReadOnlyDictionary<Guid, string> labels,
        IReadOnlySet<string>? readable)
    {
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        var labelled = active.FindEntity(task.SubjectEntityId)?.DisplayField is { } displayField
            && (readable is null || readable.Contains(displayField));
        return new TaskSubject(entity.Name, task.SubjectId, labelled ? labels.GetValueOrDefault(task.SubjectId) : null);
    }

    /// <summary>
    /// The declared names of the fields of the subject entity that <paramref name="roles"/> may
    /// read in the <paramref name="active"/> release, or null for every field. A user with no
    /// <c>read</c> grant on it, or a subject entity no longer in the release, reads no field.
    /// </summary>
    private static IReadOnlySet<string>? Readable(ApplicationModel active, IEnumerable<string> roles, Guid subjectEntityId) =>
        active.FindEntity(subjectEntityId) is { } entity
            ? PolicyEvaluator.Fields(active, roles, entity, PolicyAction.Read)
            : new HashSet<string>(StringComparer.Ordinal);

    private static IResult SignInRequired() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in to work on tasks.");

    private static IResult ApplicationNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No application is active under this name.");

    private static IResult TaskNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No task exists with this id.");

    private static IResult AssignedToSomeoneElse() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This task is assigned to someone else.");

    private static IResult AlreadyCompleted() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already completed.");

    private static IResult StaleVersion() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { ["/version"] = ["Must be the record's current version."] },
            statusCode: StatusCodes.Status409Conflict,
            title: "The record has changed since this version was read.");

    // Only the hyphenated form names a task; any other text is no task, not a bad request.
    private static bool TryParseId(string id, out Guid taskId) =>
        Guid.TryParseExact(id, "D", out taskId) && id.Length == 36;
}

/// <summary>One page of the open tasks the user may act on, and the number of them on every page.</summary>
internal sealed record TaskListResponse(IReadOnlyList<TaskListItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>An open task as a task list shows it. <see cref="DueAt"/> is null when the step has no <c>dueIn</c>.</summary>
internal sealed record TaskListItem(
    Guid Id,
    string Process,
    string Step,
    string LabelKey,
    TaskSubject Subject,
    DateTime? DueAt,
    DateTime CreatedAt);

/// <summary>The subject record of a task. <see cref="Label"/> is its display field value, or null when there is none.</summary>
internal sealed record TaskSubject(string Entity, Guid Id, string? Label);

/// <summary>The assignee of a task: the user id or the role name, and the other is null.</summary>
internal sealed record TaskAssignee(string? User, string? Role);

/// <summary>The form a task is completed on, in the shape of a form widget's metadata, with its entity's metadata.</summary>
internal sealed record TaskForm(string Name, IReadOnlyList<FormSectionMetadata> Sections, EntityMetadata Entity);

/// <summary>An outcome a task can be completed with.</summary>
internal sealed record TaskOutcome(string Name, string LabelKey);

/// <summary>One task in any state, as a task read returns it.</summary>
internal sealed record TaskResponse(
    Guid Id,
    string Process,
    Guid InstanceId,
    string Step,
    string LabelKey,
    string State,
    TaskAssignee Assignee,
    TaskSubject Subject,
    TaskForm Form,
    IReadOnlyList<TaskOutcome> Outcomes,
    DateTime? DueAt,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? CompletedBy,
    string? Outcome);
