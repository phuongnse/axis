using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Tenancy;
using Axis.Data.Forms;
using Axis.Data.Profiles;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Presentation.Sites;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Npgsql;

namespace Axis.Server.Processes;

/// <summary>
/// The task API endpoints that list the current user's open human tasks, read one task, complete
/// one, claim or release one, and reassign one. The current user is the session user. A user may act on a task assigned to their
/// id, or to a role they hold. A request is checked in order:
/// signed-in user (401), application (404), then for a list the paging (400), and for a read or a
/// completion the task id and the task's application (404) and who may act (403).
/// A completion takes the application's start lock shared first. When the active release is no
/// longer the one resolved, it runs again from resolution on the new release, and answers 503
/// after three attempts.
/// A completion then locks the task and runs <see cref="TaskCompletionCommand"/>, which checks that
/// it is open (409), then the content type (415), the body (400) and the record write of its
/// values (400, 409). One transaction writes the values, closes the task, resumes the instance
/// on the outcome's next step and appends the audit record <c>task.completed</c>. A completion
/// that fails writes nothing. A task shows its process, step, outcomes and form as the release
/// its instance is pinned to declares them, and a completion uses that release too. Its subject
/// label is the record's display field in the task's release.
/// The form state route answers what the task's form shows for a draft, without writing anything.
/// Its checks run in the task order: signed-in user (401), application, task id and task (404),
/// who may act (403), the task being open (409), then the content type (415) and the body (400).
/// It answers with the form of the task's release laid over the stored subject record, and a
/// subject record that no longer exists is a 404.
/// The claim and release routes check in the same order: signed-in user (401), application, task id
/// and task (404), who may act (403), the task being open (409). A claim then refuses a task
/// assigned to a user and a task already claimed (409). A release refuses a task that is not
/// claimed (409), and a holder who is not the claimer (403). Each runs in one transaction on the
/// locked task, takes no start lock, appends <c>task.claimed</c> or <c>task.released</c> and
/// answers with the task as a single read returns it.
/// A holder of <c>reassign</c> on the task's process in the active release may also release a claim another user
/// made, with no role on the task.
/// The reassign route checks in this order: signed-in user (401), application and task id and task (404), the
/// <c>reassign</c> grant on the task's process in the active release (403, "You have no grant for this action."
/// whoever the assignee is, and nobody holds it when the active release no longer has the process), the task being
/// open (409), the content type (415), the body (400) and the target (400). The body has exactly one of <c>user</c>
/// and <c>role</c>, and a <c>reason</c> that is not blank and at most 500 characters. A <c>role</c> that the
/// active release does not declare is a 400 at <c>/role</c>, and a <c>user</c> with no user profile is a 400 at
/// <c>/user</c>. It takes no start lock, locks the task, hands it over with <see cref="TaskReassignCommand"/>,
/// which clears the claim and appends <c>task.reassigned</c>, and answers with the task as a single read returns it.
/// A task list item and a single read say whether the task is overdue: it is open and its due date is before the
/// database's current time. A single read, and the claim, release, reassign and complete responses, also list the
/// <c>commands</c> the user may take on the task now (<c>claim</c>, <c>release</c> and <c>reassign</c>, in that order,
/// and none unless the task is open) and, when that includes <c>reassign</c>, the roles of the active release.
/// A claimed role task is listed and completed only by its claimer. Other holders may still read it
/// and ask for its form state.
/// The assignee check is the only gate: no entity grant is needed and no record filter applies.
/// A role assignee counts only when the active release declares the role, so a user whose role the
/// active release drops can no longer list, read or complete the task.
/// The user's <c>read</c> grants on the subject entity in the active release decide which fields a
/// task shows. Its form and entity leave out the fields they do not cover, matched by name, and a
/// form section left with no field is dropped. The subject label is null unless they cover the
/// display field of the task's release, and a completion refuses a value for a field they do not cover as one the form
/// does not make editable. The problem titles never contain text from the request.
/// </summary>
internal static class TaskEndpoints
{
    private const string UserProperty = "user";

    private const string RoleProperty = "role";

    private const string ReassignCommand = "reassign";

    public static void MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tasks = endpoints.MapGroup("/api/apps/{app}/tasks");
        tasks.MapGet("", ListAsync);
        tasks.MapGet("/{id}", GetAsync);
        tasks.MapPost("/{id}/complete", CompleteAsync);
        tasks.MapPost("/{id}/form-state", FormStateAsync);
        tasks.MapPost("/{id}/claim", ClaimAsync);
        tasks.MapPost("/{id}/release", ReleaseAsync);
        tasks.MapPost("/{id}/reassign", ReassignAsync);
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
            return Grants.SignInRequired();
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

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.RolesIn(active.Model));
        var connection = await database.GetConnectionAsync(cancellationToken);
        var result = await TaskQueries.ListOpenAsync(
            connection, active.Model.Manifest.Id, user.Id, roles, pageNumber, size, cancellationToken);
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var steps = new List<(ProcessTaskRow Task, ApplicationModel Release, ProcessModel Process, TaskStepModel Step)>(result.Items.Count);
        foreach (var task in result.Items)
        {
            var (release, process, step) = await TaskStepAsync(resolver, task, cancellationToken);
            steps.Add((task, release, process, step));
        }

        var labels = await ReadLabelsAsync(connection, [.. steps.Select(entry => (entry.Task, entry.Release))], cancellationToken);
        var readable = new Dictionary<Guid, IReadOnlySet<string>?>();
        var items = new List<TaskListItem>(steps.Count);
        foreach (var (task, release, process, step) in steps)
        {
            if (!readable.TryGetValue(task.SubjectEntityId, out var fields))
            {
                fields = TaskCompletionCommand.Readable(active.Model, roles, task.SubjectEntityId);
                readable[task.SubjectEntityId] = fields;
            }

            items.Add(new TaskListItem(
                task.Id,
                task.ReleaseId,
                process.Name,
                step.Name,
                step.Label.TextKey,
                Subject(release, task, labels, fields),
                task.ClaimedBy,
                task.DueAt?.UtcDateTime,
                TaskQueries.IsOverdue(task, now),
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
            return Grants.SignInRequired();
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

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.RolesIn(active.Model));
        if (!TaskQueries.MayAct(task, user.Id, roles))
        {
            return AssignedToSomeoneElse();
        }

        return Results.Ok(await DescribeAsync(resolver, connection, active.Model, user, roles, task, cancellationToken));
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
        SensitiveValues sensitive,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        // A release change under the start lock sends the completion back to resolution, so every check runs on the new release.
        for (var attempt = 0; attempt < ReleaseRetry.MaxAttempts; attempt++)
        {
            if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
            {
                return ApplicationNotFound();
            }

            if (!TryParseId(id, out var taskId))
            {
                return TaskNotFound();
            }

            var roles = PolicyEvaluator.HeldRoles(active.Model, user.RolesIn(active.Model));
            var tenantId = (tenants.Current ?? throw new InvalidOperationException("No tenant context is set.")).TenantId;
            var connection = await database.GetConnectionAsync(cancellationToken);
            await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
            {
                // The start lock comes first, before any table is touched, so a migrating activation
                // cannot deadlock with this completion. The release is read again under it.
                if (await StartLock.TakeSharedAsync(transaction, active.Model.Manifest.Id, cancellationToken) != active.ReleaseId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                // The lock makes a concurrent completion of the task wait until this one commits or
                // rolls back, and then find the task as it was left.
                var task = await TaskQueries.LockAsync(transaction, taskId, cancellationToken);
                var failure = await CompleteLockedAsync(request, resolver, connection, transaction, active, task, user, roles, tenantId, sensitive, loggerFactory, cancellationToken);
                if (failure is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return failure;
                }

                await transaction.CommitAsync(cancellationToken);
            }

            var completed = await TaskQueries.FindAsync(connection, taskId, cancellationToken)
                ?? throw new InvalidOperationException($"The completed task '{taskId}' could not be read.");
            return Results.Ok(await DescribeAsync(resolver, connection, active.Model, user, roles, completed, cancellationToken));
        }

        return ReleaseRetry.Changed();
    }

    // The body is read here rather than bound, so every body problem is reported by JSON Pointer.
    // Nothing is written, so the route takes no start lock and opens no transaction.
    private static async Task<IResult> FormStateAsync(
        string app,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return ApplicationNotFound();
        }

        if (!TryParseId(id, out var taskId))
        {
            return TaskNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await TaskQueries.FindAsync(connection, taskId, cancellationToken) is not { } task
            || task.ApplicationId != active.Model.Manifest.Id)
        {
            return TaskNotFound();
        }

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.RolesIn(active.Model));
        if (!TaskQueries.MayAct(task, user.Id, roles))
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
        var form = TaskCompletionCommand.FormOf(release, task, step);
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        var body = FormStateBody.Parse(await RecordEndpoints.ReadBodyAsync(request, cancellationToken), entity, release, allowRecordId: false);
        if (body.Malformed is { } malformed)
        {
            return Results.ValidationProblem(malformed);
        }

        if (await RecordQueries.GetAsync(connection, release, entity, task.SubjectId, cancellationToken) is not { } stored)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No record has this id.");
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var readable = TaskCompletionCommand.Readable(active.Model, roles, task.SubjectEntityId);
        var sets = await RecordSets.ReadAsync(connection, release, entity, stored, body.Draft!.Input.Links, cancellationToken);
        return Results.Ok(FormStateEvaluator.Evaluate(release, form, entity, stored, body.Draft!, body.Touched!, readable, user.Id, now, sets));
    }

    private static Task<IResult> ClaimAsync(
        string app,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken) =>
        ChangeClaimAsync(app, id, httpContext, resolver, database,
            (transaction, applicationId, task, actor, _, ct) => TaskClaimCommand.ClaimLockedAsync(transaction, applicationId, task, actor, ct),
            cancellationToken);

    private static Task<IResult> ReleaseAsync(
        string app,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken) =>
        ChangeClaimAsync(app, id, httpContext, resolver, database, TaskClaimCommand.ReleaseLockedAsync, cancellationToken);

    // A claim or a release only changes the task row and writes an audit record. It never resumes
    // the instance, so it takes no start lock and does not depend on the active release changing.
    private static async Task<IResult> ChangeClaimAsync(
        string app,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        Func<NpgsqlTransaction, Guid, ProcessTaskRow?, RecordActor, bool, CancellationToken, Task<TaskClaimOutcome>> change,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return ApplicationNotFound();
        }

        if (!TryParseId(id, out var taskId))
        {
            return TaskNotFound();
        }

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.RolesIn(active.Model));
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var task = await TaskQueries.LockAsync(transaction, taskId, cancellationToken);
            var mayReassign = task is not null && MayReassign(active.Model, user, task);
            var outcome = await change(
                transaction, active.Model.Manifest.Id, task, new RecordActor(user.Id, roles), mayReassign, cancellationToken);
            if (outcome != TaskClaimOutcome.Done)
            {
                await transaction.RollbackAsync(cancellationToken);
                return outcome switch
                {
                    TaskClaimOutcome.NotFound => TaskNotFound(),
                    TaskClaimOutcome.Denied => AssignedToSomeoneElse(),
                    TaskClaimOutcome.NotOpen => AlreadyCompleted(),
                    TaskClaimOutcome.NotRoleTask => NotARoleTask(),
                    TaskClaimOutcome.AlreadyClaimed => AlreadyClaimed(),
                    _ => NotClaimed(),
                };
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var changed = await TaskQueries.FindAsync(connection, taskId, cancellationToken)
            ?? throw new InvalidOperationException($"The task '{taskId}' could not be read.");
        return Results.Ok(await DescribeAsync(resolver, connection, active.Model, user, roles, changed, cancellationToken));
    }

    // Like a claim, a reassignment only changes the task row and writes an audit record, so it takes no start lock.
    // The grant is checked before the body, so a caller without it learns nothing about the target.
    private static async Task<IResult> ReassignAsync(
        string app,
        string id,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return ApplicationNotFound();
        }

        if (!TryParseId(id, out var taskId))
        {
            return TaskNotFound();
        }

        var roles = PolicyEvaluator.HeldRoles(active.Model, user.RolesIn(active.Model));
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var task = await TaskQueries.LockAsync(transaction, taskId, cancellationToken);
            var failure = await ReassignLockedAsync(request, connection, transaction, active, task, user, roles, cancellationToken);
            if (failure is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return failure;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var changed = await TaskQueries.FindAsync(connection, taskId, cancellationToken)
            ?? throw new InvalidOperationException($"The task '{taskId}' could not be read.");
        return Results.Ok(await DescribeAsync(resolver, connection, active.Model, user, roles, changed, cancellationToken));
    }

    /// <summary>
    /// Runs the checks of a reassignment on the locked <paramref name="task"/> and hands it over, or
    /// returns the failure, after which the caller rolls every write back.
    /// </summary>
    private static async Task<IResult?> ReassignLockedAsync(
        HttpRequest request,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActiveApplication active,
        ProcessTaskRow? task,
        CurrentUserResponse user,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        // A task of another application does not exist under this one, so it is a 404 as well.
        if (task is null || task.ApplicationId != active.Model.Manifest.Id)
        {
            return TaskNotFound();
        }

        if (!MayReassign(active.Model, user, task))
        {
            return Grants.Forbidden();
        }

        if (task.State != ProcessTasks.Open)
        {
            return AlreadyCompleted();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var (body, errors) = ProcessOperatorEndpoints.ReadObject(
            await RecordEndpoints.ReadBodyAsync(request, cancellationToken),
            [UserProperty, RoleProperty, ProcessOperatorEndpoints.ReasonProperty],
            ReadReassignBody);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        if (body!.Kind == ProcessTasks.RoleAssignee)
        {
            if (!active.Model.Roles.Any(role => string.Equals(role.Name, body.Assignee, StringComparison.Ordinal)))
            {
                return TargetNotFound("/" + RoleProperty, "No role with this name is declared.");
            }
        }
        else if (!await UserProfiles.ExistsAsync(connection, transaction, body.Assignee, cancellationToken))
        {
            return TargetNotFound("/" + UserProperty, "No user with this id has signed in.");
        }

        var outcome = await TaskReassignCommand.ReassignLockedAsync(
            transaction,
            active.Model.Manifest.Id,
            task,
            body.Kind,
            body.Assignee,
            body.Reason,
            new RecordActor(user.Id, roles),
            cancellationToken);
        return outcome switch
        {
            TaskReassignOutcome.Done => null,
            TaskReassignOutcome.NotFound => TaskNotFound(),
            _ => AlreadyCompleted(),
        };
    }

    // Exactly one of user and role is the target. Both or neither is one problem on the whole body.
    private static ReassignBody? ReadReassignBody(Dictionary<string, JsonElement> properties, SortedDictionary<string, string[]> errors)
    {
        var reason = ProcessOperatorEndpoints.ReadReasonText(properties, errors);
        var hasUser = properties.ContainsKey(UserProperty);
        var hasRole = properties.ContainsKey(RoleProperty);
        if (hasUser == hasRole)
        {
            errors[""] = ["Must have exactly one of user and role."];
            return null;
        }

        var property = hasUser ? UserProperty : RoleProperty;
        var assignee = properties[property].ValueKind == JsonValueKind.String ? properties[property].GetString() : null;
        if (string.IsNullOrWhiteSpace(assignee))
        {
            errors["/" + property] = ["Required."];
            return null;
        }

        return reason is null ? null : new ReassignBody(hasUser ? ProcessTasks.UserAssignee : ProcessTasks.RoleAssignee, assignee, reason);
    }

    /// <summary>
    /// Completes the locked <paramref name="task"/> in <paramref name="transaction"/> with
    /// <see cref="TaskCompletionCommand"/>, or returns the failure, after which the caller rolls
    /// every write back.
    /// </summary>
    private static async Task<IResult?> CompleteLockedAsync(
        HttpRequest request,
        ActiveApplicationResolver resolver,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActiveApplication current,
        ProcessTaskRow? task,
        CurrentUserResponse user,
        IReadOnlyList<string> roles,
        string tenantId,
        SensitiveValues sensitive,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var result = await TaskCompletionCommand.CompleteLockedAsync(
            connection,
            transaction,
            tenantId,
            current.Model,
            current.ReleaseId,
            task,
            new RecordActor(user.Id, roles),
            (completing, ct) => resolver.GetReleaseModelAsync(completing.ReleaseId, ct),
            async ct => JsonRequest.IsJson(request) ? await RecordEndpoints.ReadBodyAsync(request, ct) : null,
            new RecordCipher(sensitive, tenantId),
            loggerFactory,
            cancellationToken);
        return result.Outcome switch
        {
            TaskCompletionOutcome.Completed => null,
            TaskCompletionOutcome.NotFound => TaskNotFound(),
            TaskCompletionOutcome.Denied => AssignedToSomeoneElse(),
            TaskCompletionOutcome.AlreadyCompleted => AlreadyCompleted(),
            TaskCompletionOutcome.UnsupportedContent => JsonRequest.UnsupportedMediaType(),
            TaskCompletionOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            TaskCompletionOutcome.StaleVersion => StaleVersion(),
            _ => RecordEndpoints.WriteFailure(result.Write!),
        };
    }

    /// <summary>
    /// The task as a single read returns it, with its form as its release declares it, less the
    /// fields <paramref name="roles"/> may not read.
    /// </summary>
    private static async Task<TaskResponse> DescribeAsync(
        ActiveApplicationResolver resolver,
        NpgsqlConnection connection,
        ApplicationModel active,
        CurrentUserResponse user,
        IReadOnlyList<string> roles,
        ProcessTaskRow task,
        CancellationToken cancellationToken)
    {
        var (release, process, step) = await TaskStepAsync(resolver, task, cancellationToken);
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var commands = Commands(task, user.Id, roles, MayReassign(active, user, task));
        var labels = await ReadLabelsAsync(connection, [(task, release)], cancellationToken);
        var formModel = TaskCompletionCommand.FormOf(release, task, step);
        var form = ApplicationSites.DescribeForm(release, formModel.Name);
        var entity = ApplicationSites.DescribeEntity(release, formModel.Entity.Name);
        var readable = TaskCompletionCommand.Readable(active, roles, task.SubjectEntityId);
        if (readable is not null)
        {
            form = ApplicationSites.Readable(form, readable);
            entity = entity with { Fields = [.. entity.Fields.Where(field => readable.Contains(field.Name))] };
        }

        return new TaskResponse(
            task.Id,
            task.ReleaseId,
            process.Name,
            task.ProcessInstanceId,
            step.Name,
            step.Label.TextKey,
            task.State,
            new TaskAssignee(
                task.AssigneeKind == ProcessTasks.UserAssignee ? task.Assignee : null,
                task.AssigneeKind == ProcessTasks.RoleAssignee ? task.Assignee : null),
            task.ClaimedBy,
            Subject(release, task, labels, readable),
            new TaskForm(form.Name, form.Sections, form.Steps, entity),
            [.. step.Outcomes.Select(outcome => new TaskOutcome(outcome.Name, outcome.Label.TextKey))],
            task.DueAt?.UtcDateTime,
            TaskQueries.IsOverdue(task, now),
            task.CreatedAt.UtcDateTime,
            task.CompletedAt?.UtcDateTime,
            task.CompletedBy,
            task.Outcome,
            commands,
            commands.Contains(ReassignCommand)
                ? [.. active.Roles.OrderBy(role => role.Name, StringComparer.Ordinal).Select(role => new TaskRole(role.Name, role.Label?.TextKey))]
                : []);
    }

    /// <summary>Whether <paramref name="user"/> holds <c>reassign</c> on the process of <paramref name="task"/> in the <paramref name="active"/> release.</summary>
    private static bool MayReassign(ApplicationModel active, CurrentUserResponse user, ProcessTaskRow task) =>
        active.Processes.FirstOrDefault(process => process.Id == task.ProcessId) is { } process
        && PolicyEvaluator.AllowsReassign(active, user.RolesIn(active), process);

    /// <summary>
    /// The commands the user may take on <paramref name="task"/> now, in the order <c>claim</c>,
    /// <c>release</c>, <c>reassign</c>. Each mirrors what its route accepts, and none is listed unless the task is open.
    /// </summary>
    private static List<string> Commands(ProcessTaskRow task, string userId, IReadOnlyList<string> roles, bool mayReassign)
    {
        var commands = new List<string>();
        if (task.State != ProcessTasks.Open)
        {
            return commands;
        }

        var mayAct = TaskQueries.MayAct(task, userId, roles);
        if (task.AssigneeKind == ProcessTasks.RoleAssignee && task.ClaimedBy is null && mayAct)
        {
            commands.Add("claim");
        }

        if (task.ClaimedBy is not null
            && (mayReassign || (mayAct && string.Equals(task.ClaimedBy, userId, StringComparison.Ordinal))))
        {
            commands.Add("release");
        }

        if (mayReassign)
        {
            commands.Add(ReassignCommand);
        }

        return commands;
    }

    /// <summary>The model of the task's release, with the task's process and step as that release declares them.</summary>
    private static async Task<(ApplicationModel Release, ProcessModel Process, TaskStepModel Step)> TaskStepAsync(
        ActiveApplicationResolver resolver, ProcessTaskRow task, CancellationToken cancellationToken)
    {
        var release = await resolver.GetReleaseModelAsync(task.ReleaseId, cancellationToken);
        var (process, step) = TaskCompletionCommand.StepOf(release, task);
        return (release, process, step);
    }

    /// <summary>
    /// The subject labels of <paramref name="tasks"/>, by release id and subject record id, read
    /// through the display field of each task's own release with one query per release and subject
    /// entity. A subject whose entity or display field is not in its release has no label.
    /// </summary>
    private static async Task<IReadOnlyDictionary<(Guid ReleaseId, Guid SubjectId), string>> ReadLabelsAsync(
        NpgsqlConnection connection,
        IReadOnlyList<(ProcessTaskRow Task, ApplicationModel Release)> tasks,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<(Guid, Guid), string>();
        foreach (var group in tasks.GroupBy(entry => (entry.Task.ReleaseId, entry.Task.SubjectEntityId)))
        {
            if (group.First().Release.FindEntity(group.Key.SubjectEntityId) is not { } entity)
            {
                continue;
            }

            var ids = group.Select(entry => entry.Task.SubjectId).Distinct().ToList();
            foreach (var (subjectId, label) in await RecordQueries.ReadDisplayLabelsAsync(connection, entity, ids, cancellationToken))
            {
                labels[(group.Key.ReleaseId, subjectId)] = label;
            }
        }

        return labels;
    }

    /// <summary>
    /// The subject of <paramref name="task"/>, with its label only when <paramref name="readable"/>
    /// covers the display field of the subject entity in the task's <paramref name="release"/>.
    /// <paramref name="readable"/> comes from the active release.
    /// </summary>
    private static TaskSubject Subject(
        ApplicationModel release,
        ProcessTaskRow task,
        IReadOnlyDictionary<(Guid ReleaseId, Guid SubjectId), string> labels,
        IReadOnlySet<string>? readable)
    {
        var entity = release.FindEntity(task.SubjectEntityId)
            ?? throw new InvalidOperationException($"The subject entity of task '{task.Id}' is not in its release.");
        var labelled = entity.DisplayField is { } displayField && (readable is null || readable.Contains(displayField));
        return new TaskSubject(entity.Name, task.SubjectId, labelled ? labels.GetValueOrDefault((task.ReleaseId, task.SubjectId)) : null);
    }

    private static IResult ApplicationNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No application is active under this name.");

    private static IResult TaskNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No task exists with this id.");

    private static IResult AssignedToSomeoneElse() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "This task is assigned to someone else.");

    private static IResult AlreadyCompleted() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already completed.");

    private static IResult NotARoleTask() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Only a task assigned to a role can be claimed.");

    private static IResult AlreadyClaimed() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is already claimed.");

    private static IResult NotClaimed() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This task is not claimed.");

    private static IResult TargetNotFound(string pointer, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [pointer] = [message] });

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

/// <summary>An open task as a task list shows it, with the release it started on. <see cref="ClaimedBy"/> is the claimer's user id, or null. <see cref="DueAt"/> is null when the step has no <c>dueIn</c>. <see cref="Overdue"/> is true when <see cref="DueAt"/> has passed.</summary>
internal sealed record TaskListItem(
    Guid Id,
    Guid ReleaseId,
    string Process,
    string Step,
    string LabelKey,
    TaskSubject Subject,
    string? ClaimedBy,
    DateTime? DueAt,
    bool Overdue,
    DateTime CreatedAt);

/// <summary>The subject record of a task. <see cref="Label"/> is its display field value, or null when there is none.</summary>
internal sealed record TaskSubject(string Entity, Guid Id, string? Label);

/// <summary>The body of a reassignment: whether the target is a user or a role, its id or name, and the reason.</summary>
internal sealed record ReassignBody(string Kind, string Assignee, string Reason);

/// <summary>The assignee of a task: the user id or the role name, and the other is null.</summary>
internal sealed record TaskAssignee(string? User, string? Role);

/// <summary>The form a task is completed on, in the shape of a form widget's metadata, with its entity's metadata.</summary>
internal sealed record TaskForm(string Name, IReadOnlyList<FormSectionMetadata> Sections, IReadOnlyList<FormStepMetadata> Steps, EntityMetadata Entity);

/// <summary>An outcome a task can be completed with.</summary>
internal sealed record TaskOutcome(string Name, string LabelKey);

/// <summary>
/// One task in any state, as a task read returns it. <see cref="Overdue"/> is true only for an open task whose
/// <see cref="DueAt"/> has passed. <see cref="Commands"/> are the actions the user may take on it now, and
/// <see cref="ReassignRoles"/> the roles it can be reassigned to, empty unless <see cref="Commands"/> has <c>reassign</c>.
/// </summary>
internal sealed record TaskResponse(
    Guid Id,
    Guid ReleaseId,
    string Process,
    Guid InstanceId,
    string Step,
    string LabelKey,
    string State,
    TaskAssignee Assignee,
    string? ClaimedBy,
    TaskSubject Subject,
    TaskForm Form,
    IReadOnlyList<TaskOutcome> Outcomes,
    DateTime? DueAt,
    bool Overdue,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? CompletedBy,
    string? Outcome,
    IReadOnlyList<string> Commands,
    IReadOnlyList<TaskRole> ReassignRoles);

/// <summary>A role a task can be reassigned to, with its label's text key, or null when it has none.</summary>
internal sealed record TaskRole(string Name, string? LabelKey);
