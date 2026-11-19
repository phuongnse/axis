using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Core.Tenancy;
using Axis.Data.Profiles;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Storage;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Studio;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoints for a user who may inspect a process. The list at
/// <c>GET /api/apps/{app}/processes/{process}/instances</c> shows the instances of the process,
/// newest first and paged as the record API pages, whatever release each is pinned to. It takes the record API's <c>sort</c>,
/// <c>search</c> and <c>filter</c> through <see cref="ListQuery{T}"/>, which PostgreSQL applies, over <c>subjectLabel</c>,
/// <c>state</c>, <c>step</c>, <c>releaseNumber</c>, <c>createdAt</c> and <c>endedAt</c>. <c>subjectLabel</c> is a field only for a
/// process with a subject whose display field the user may read and the active release does not mark sensitive, so otherwise
/// a sort or filter on it is a 400 and a search never matches it. A search looks in <c>subjectLabel</c> and <c>step</c>. A sorted list
/// breaks ties newest first, and the total counts only the instances that match the filter and the search. The
/// <c>state</c> parameter may repeat and keeps the instances in any of the given states, and <c>subject</c> keeps the instances of one record. A
/// request is checked in order: session or service account bearer token (401), application and process (404), the user's
/// <c>inspect</c> grant on the process (403), then the parameters (400), each keyed by its name.
/// An instance is listed only when its subject record exists and matches one of the user's read
/// filters on the subject entity in the active release. The user's <c>read</c> grant on that
/// entity is needed for it, so without one the list is empty. A hidden instance is in neither the
/// page nor the total. A read filter that fails at run time is a 400. A <c>read</c> filter that
/// reads a computed field that is being recomputed is a 409 that names no field or policy, after the
/// parameters and before the instances are read. Each item has the <c>subjectLabel</c>, the subject record's display field
/// value, unless the user's read grant does not cover the field or either release marks it sensitive, and the pinned release's
/// <c>releaseNumber</c>. The list writes no audit record. The problem titles never contain text from the request.
/// <para>
/// The detail at <c>GET /api/apps/{app}/processes/{process}/instances/{id}</c> shows one instance:
/// its properties, the graph of the release it is pinned to, its path (the history rows in order),
/// its tasks, its calls and the operator <c>commands</c> the user may take on it. While the instance waits on a timer step, the
/// detail also has <c>timerDueAt</c>, the due time of the timer's work item. A call lists its attempts and lookups, and its stored input only when
/// the user may read every field that the step's input and output mapping reads or writes, on any
/// entity, and none of them is sensitive in the pinned or the active release. Otherwise the input is
/// null and marked hidden. The commands come from the user's <c>retry</c> and <c>cancel</c> grants and the states, and a call is
/// <c>resolvable</c> with its declared <c>outputs</c> by the <c>resolve</c> grant, so two users can see one instance differently. The pinned release gives the fields the
/// mapping uses and the active release gives the fields the user may read. The checks run in the order of the list, then the id
/// is parsed. A <c>read</c> filter that reads a computed field that is being recomputed is a 409,
/// as in the list, before any instance is looked up. A malformed id, an unknown id, an instance of another process, and an instance whose
/// subject record the user may not read or that no longer exists are one 404. A read filter that
/// fails at run time for the subject record does not match, so it is a 404 too. The detail writes
/// no audit record. The detail also names the subject entity (<c>subjectEntity</c>), the label (<c>subjectLabel</c>, under the list's rules) and
/// the pinned release's number (<c>releaseNumber</c>), and gives <c>studio</c>, <c>{ app, resource }</c>, to a user who holds the author role of
/// <c>Studio:AuthorRole</c> and null to anyone else. A task gives <c>dueAt</c>, <c>assigneeId</c> and <c>completedById</c>, and its <c>assignee</c>
/// and <c>completedBy</c> are a person's name, or the user id when there is no profile, and a role assignee keeps its role's name.
/// </para>
/// <para>
/// A process without a subject has no record to read. Its instances are listed and read with the
/// <c>inspect</c> grant alone: <c>subjectId</c> is null, a <c>subject</c> filter matches nothing, and
/// no read grant, read filter or pending field applies. The detail of such an instance has no
/// <c>commands</c> and no resolvable call, because the operator commands do not serve it yet, and a
/// stored call input is always hidden.
/// </para>
/// </summary>
internal static class ProcessInspectionEndpoints
{
    private static readonly string[] _states =
    [
        ProcessStarts.Running,
        ProcessStarts.Waiting,
        ProcessStarts.Completed,
        ProcessStarts.Failed,
        ProcessStarts.Cancelled,
    ];

    public static void MapProcessInspectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/apps/{app}/processes/{process}/instances", ListAsync).WithMetadata(ServiceAccountRoute.Instance);
        endpoints.MapGet("/api/apps/{app}/processes/{process}/instances/{id}", GetAsync).WithMetadata(ServiceAccountRoute.Instance);
    }

    // The query parameters are bound as strings so every invalid one is reported in one problem. The repeated `state` needs
    // [FromQuery], because the request delegate generator does not map the route without it.
    private static async Task<IResult> ListAsync(
        string app,
        string process,
        [FromQuery] string[]? state,
        string? subject,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        SensitiveValues sensitive,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return NotFound("No application is active under this name.");
        }

        var application = active.Model;
        if (!application.TryGetProcess(process, out var model))
        {
            return NotFound("The application has no process with this name.");
        }

        if (!PolicyEvaluator.AllowsInspect(application, user.RolesIn(application), model))
        {
            return Grants.Forbidden();
        }

        // The subject entity comes first, because it decides whether the label is a field of the list.
        EntityModel? entity = null;
        if (model.Entity is { } subjectEntity && !application.TryGetEntity(subjectEntity.Name, out entity))
        {
            throw new InvalidOperationException($"The subject entity '{subjectEntity.Name}' of process '{model.Name}' is not in the model.");
        }

        var readable = entity is null ? null : PolicyEvaluator.Fields(application, user.RolesIn(application), entity, PolicyAction.Read);
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var query = ListQuery<InstanceListItem>.Read(httpContext.Request, Columns(entity, readable), errors);
        if (state is not null && state.Any(value => !_states.Contains(value, StringComparer.Ordinal)))
        {
            errors["state"] = [$"Must be one of {string.Join(", ", _states)}."];
        }

        // A missing state is an empty array, which is no filter.
        IReadOnlyList<string>? states = state is { Length: > 0 } ? state : null;
        Guid? subjectId = null;
        if (subject is not null)
        {
            if (subject is { Length: 36 } && Guid.TryParseExact(subject, "D", out var parsed))
            {
                subjectId = parsed;
            }
            else
            {
                errors["subject"] = ["Must be the id of a record."];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // An instance of a process without a subject has no record to read, so the inspect grant is enough.
        if (entity is null)
        {
            var withoutSubject = await InstanceQueries.ListWithoutSubjectAsync(
                await database.GetConnectionAsync(cancellationToken),
                application.Manifest.Id,
                model.Id,
                states,
                subjectId,
                query.Sql(),
                query.Page,
                query.PageSize,
                cancellationToken);
            return Results.Ok(new ProcessInstanceListResponse(
                withoutSubject.Items.Select(item => ListItem(item, showsLabel: false)).ToList(), query.Page, query.PageSize, withoutSubject.TotalCount));
        }

        // An instance whose record the user may not read does not exist for them.
        var read = PolicyEvaluator.Access(application, user.RolesIn(application), entity, PolicyAction.Read);
        if (!read.Granted)
        {
            return Results.Ok(new ProcessInstanceListResponse([], query.Page, query.PageSize, 0));
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, null, application, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        var condition = read.Filters is { } filters
            ? await RecordFilters.ConditionAsync(application, entity, RecordCommand.Expressions(filters), user.Id, RecordEndpoints.Cipher(sensitive, tenants), cancellationToken)
            : null;
        if (await InstanceQueries.ListAsync(
                connection, application.Manifest.Id, model.Id, entity, states, subjectId, condition, query.Sql(), query.Page, query.PageSize, cancellationToken) is not { } result)
        {
            // PostgreSQL cannot fail one branch of an OR, so every filter of the list is named.
            var logger = loggerFactory.CreateLogger(typeof(ProcessInspectionEndpoints).FullName!);
            foreach (var filter in read.Filters!)
            {
                RecordFilterLog.ListFailed(logger, filter.Policy, filter.Grant, entity.Name);
            }

            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A record filter could not be evaluated for these records.");
        }

        // The query reads the display field of every listed instance. The label shows only when the user may
        // read the field and neither the active nor the pinned release marks it sensitive.
        var pinnedEntities = new Dictionary<Guid, EntityModel?>();
        var items = new List<ProcessInstanceListItem>(result.Items.Count);
        foreach (var item in result.Items)
        {
            var releaseId = item.Instance.ReleaseId;
            if (!pinnedEntities.TryGetValue(releaseId, out var pinned))
            {
                pinned = (await resolver.GetReleaseModelAsync(releaseId, cancellationToken)).FindEntity(entity.Id);
                pinnedEntities[releaseId] = pinned;
            }

            items.Add(ListItem(item, ShowsSubjectLabel(entity, pinned, readable)));
        }

        return Results.Ok(new ProcessInstanceListResponse(items, query.Page, query.PageSize, result.TotalCount));
    }

    // The columns the list's search, filter and sort can name, over axis.process_instances. The Value functions only satisfy the
    // column record, as in CellEndpoints, because PostgreSQL applies the list. The subject label is a column only when the
    // user may see it. The time columns filter by their UTC day and sort by their instant.
    private static List<ListColumn<InstanceListItem>> Columns(EntityModel? entity, IReadOnlySet<string>? readable)
    {
        var columns = new List<ListColumn<InstanceListItem>>();
        if (entity is not null && ShowsSubjectLabel(entity, null, readable))
        {
            columns.Add(new("subjectLabel", ListColumnKind.Text, item => item.SubjectLabel, Searchable: true, Sql: InstanceQueries.SubjectLabelSql(entity)));
        }

        columns.Add(new("state", ListColumnKind.Enum, item => item.Instance.State, Values: _states, Sql: "i.state"));
        columns.Add(new("step", ListColumnKind.Text, item => item.Instance.Step, Searchable: true, Sql: "i.step"));
        columns.Add(new("releaseNumber", ListColumnKind.Number, item => item.ReleaseNumber, Sql: InstanceQueries.ReleaseNumberSql));
        columns.Add(new(
            "createdAt",
            ListColumnKind.Date,
            item => DateOnly.FromDateTime(item.Instance.CreatedAt.UtcDateTime),
            Order: item => item.Instance.CreatedAt,
            Sql: "(i.created_at AT TIME ZONE 'UTC')::date",
            OrderSql: "i.created_at"));
        columns.Add(new(
            "endedAt",
            ListColumnKind.Date,
            item => item.Instance.EndedAt is { } endedAt ? DateOnly.FromDateTime(endedAt.UtcDateTime) : null,
            Order: item => item.Instance.EndedAt,
            Sql: "(i.ended_at AT TIME ZONE 'UTC')::date",
            OrderSql: "i.ended_at"));
        return columns;
    }

    private static ProcessInstanceListItem ListItem(InstanceListItem item, bool showsLabel) =>
        new(
            item.Instance.Id,
            item.Instance.SubjectId,
            showsLabel ? item.SubjectLabel : null,
            item.Instance.State,
            item.Instance.Step,
            item.Instance.ReleaseId,
            item.ReleaseNumber,
            item.Instance.CreatedAt.UtcDateTime,
            item.Instance.EndedAt?.UtcDateTime);

    // The subject label shows when the entity has a display field that the user's read grant covers
    // and that neither release marks sensitive.
    internal static bool ShowsSubjectLabel(EntityModel active, EntityModel? pinned, IReadOnlySet<string>? readable)
    {
        if (active.DisplayField is not { } displayField || (readable is not null && !readable.Contains(displayField)))
        {
            return false;
        }

        foreach (var release in new[] { active, pinned })
        {
            if (release is not null && release.TryGetField(displayField, out var field) && field.Sensitive)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<IResult> GetAsync(
        string app,
        string process,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        PackageCatalog packages,
        StudioOptions studio,
        SensitiveValues sensitive,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return NotFound("No application is active under this name.");
        }

        var application = active.Model;
        if (!application.TryGetProcess(process, out var model))
        {
            return NotFound("The application has no process with this name.");
        }

        if (!PolicyEvaluator.AllowsInspect(application, user.RolesIn(application), model))
        {
            return Grants.Forbidden();
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        EntityModel? entity = null;
        ProcessInstanceRow? row;
        if (model.Entity is not { } subjectEntity)
        {
            // An instance of a process without a subject has no record to read, so the inspect grant is enough.
            row = await InstanceQueries.FindWithoutSubjectAsync(connection, application.Manifest.Id, model.Id, instanceId, cancellationToken);
        }
        else
        {
            if (!application.TryGetEntity(subjectEntity.Name, out entity))
            {
                throw new InvalidOperationException($"The subject entity '{subjectEntity.Name}' of process '{model.Name}' is not in the model.");
            }

            // An instance whose record the user may not read does not exist for them.
            var read = PolicyEvaluator.Access(application, user.RolesIn(application), entity, PolicyAction.Read);
            if (!read.Granted)
            {
                return InstanceNotFound();
            }

            if (read.Filters is { } pendingFilters
                && await RecordCommand.ReadsPendingAsync(connection, null, application, entity, pendingFilters, loggerFactory, cancellationToken))
            {
                return Grants.PendingFilter();
            }

            row = await InstanceQueries.FindAsync(connection, application.Manifest.Id, model.Id, entity, instanceId, cancellationToken);

            // FindAsync filters by the subject entity, so the instance has a subject.
            if (row is not null
                && read.Filters is { } filters
                && await RecordCommand.MatchAsync(connection, null, application, entity, row.SubjectId!.Value, filters, user.Id, RecordEndpoints.Cipher(sensitive, tenants), loggerFactory, cancellationToken) is not { Found: true, Matched: true })
            {
                row = null;
            }
        }

        if (row is null)
        {
            return InstanceNotFound();
        }

        return Results.Ok(await DetailAsync(
            connection, resolver, packages, application, model, entity, row, InspectionViewer.ForUser(user.RolesIn(application)), studio.Allows(user.Roles), cancellationToken));
    }

    /// <summary>
    /// Builds the detail of <paramref name="row"/> for <paramref name="viewer"/>. <paramref name="active"/> is the active release
    /// of the instance's application, or its pinned release when the application has none. <paramref name="process"/> and
    /// <paramref name="subject"/> are the process and the subject entity in that release. The caller has checked that the viewer
    /// may read the instance, so this applies no read grant, filter or subject check of its own. <paramref name="author"/> says
    /// whether the viewer holds the author role, which gives the detail its <c>studio</c> address.
    /// </summary>
    internal static async Task<ProcessInstanceDetailResponse> DetailAsync(
        NpgsqlConnection connection,
        ActiveApplicationResolver resolver,
        PackageCatalog packages,
        ApplicationModel active,
        ProcessModel process,
        EntityModel? subject,
        ProcessInstanceRow row,
        InspectionViewer viewer,
        bool author,
        CancellationToken cancellationToken)
    {
        // The due time shows only while the instance waits, from the timer item of its current revision.
        var timerDueAt = row.State == ProcessStarts.Waiting
            ? await InstanceQueries.TimerDueAtAsync(connection, row.Id, row.Revision, cancellationToken)
            : null;

        // A parent or sub-process is named only when the user may inspect its process in the active release.
        // The subject record's read filters do not apply, and no subject id is shown.
        var relatives = await InstanceQueries.RelativesAsync(connection, row.ApplicationId, row.Id, cancellationToken);
        var parentProcess = relatives.Parent is { } relatedParent ? InspectableProcess(active, viewer, relatedParent.ProcessId) : null;
        var parent = relatives.Parent is { } parentRow && parentProcess is not null
            ? new ProcessInstanceParent(parentRow.Id, parentProcess.Name, parentRow.Step)
            : null;
        var subProcesses = new List<ProcessSubProcess>();
        foreach (var child in relatives.Children)
        {
            if (InspectableProcess(active, viewer, child.ProcessId) is { } childProcess)
            {
                subProcesses.Add(new ProcessSubProcess(
                    child.Id,
                    childProcess.Name,
                    child.Step,
                    child.State,
                    child.CreatedAt.UtcDateTime,
                    child.EndedAt?.UtcDateTime));
            }
        }

        // The graph is the pinned release's, so a newer release does not change a running instance.
        var pinned = await resolver.GetReleaseModelAsync(row.ReleaseId, cancellationToken);
        var graph = Graph(pinned.Processes.Single(candidate => candidate.Id == process.Id));
        var history = await InstanceQueries.HistoryAsync(connection, row.Id, cancellationToken);
        var tasks = await TaskQueries.ListByInstanceAsync(connection, row.Id, cancellationToken);
        var calls = await CallQueries.ListByInstanceAsync(connection, row.Id, cancellationToken);
        var mappings = new Dictionary<string, IReadOnlySet<MappedField>?>(StringComparer.Ordinal);
        var pinnedProcess = pinned.Processes.Single(candidate => candidate.Id == process.Id);
        var pinnedSubject = subject is null ? null : pinned.FindEntity(subject.Id);

        // The label and the release number follow the list's rules, so a label the viewer may not read is null. The console
        // falls back to the pinned entity when the active release dropped it, and then the label is not read.
        var showsLabel = subject is not null
            && active.FindEntity(subject.Id) is not null
            && ShowsSubjectLabel(subject, pinnedSubject, viewer.ReadableFields(active, subject));
        var labels = await InstanceQueries.LabelsAsync(connection, row.Id, showsLabel ? subject : null, cancellationToken);

        // A user assignee, the one who claimed a role task and the one who completed a task are shown by name.
        var userIds = tasks
            .SelectMany(task => new[] { task.AssigneeKind == ProcessTasks.UserAssignee ? task.Assignee : null, task.ClaimedBy, task.CompletedBy })
            .OfType<string>()
            .ToList();
        var names = await UserProfiles.FindDisplayNamesAsync(connection, userIds, cancellationToken);
        string Name(string userId) => names.GetValueOrDefault(userId) ?? userId;

        // The values the instance started with and ended with, each masked on its own.
        var input = subject is null ? await InputAsync(connection, pinned, pinnedProcess, active, viewer, row, cancellationToken) : null;
        var result = row.State == ProcessStarts.Completed
            ? await ResultAsync(connection, pinned, active, subject, viewer, row, cancellationToken)
            : null;

        // The operator commands do not serve an instance without a subject yet, so the detail
        // offers none of them and no call is resolvable.
        var allowsResolve = subject is not null && viewer.AllowsResolve(active, process);
        var commands = new List<string>();
        if (subject is not null && row.State == ProcessStarts.Failed && viewer.AllowsRetry(active, process))
        {
            commands.Add("retry");
        }

        if (subject is not null
            && row.State is ProcessStarts.Running or ProcessStarts.Waiting
            && !calls.Any(call => call.State is ConnectorCalls.Sending or ConnectorCalls.Unknown)
            && viewer.AllowsCancel(active, process))
        {
            commands.Add("cancel");
        }

        return new ProcessInstanceDetailResponse(
            row.Id,
            process.Name,
            subject?.Name,
            row.SubjectId,
            labels.SubjectLabel,
            row.ReleaseId,
            labels.ReleaseNumber,
            row.State,
            row.Step,
            row.CreatedAt.UtcDateTime,
            row.EndedAt?.UtcDateTime,
            timerDueAt?.UtcDateTime,
            input,
            result,
            parent,
            subProcesses,
            graph,
            history
                .Select(entry => new ProcessPathEntry(
                    entry.Step,
                    entry.StartedAt.UtcDateTime,
                    entry.FinishedAt.UtcDateTime,
                    entry.Decision,
                    entry.Output is null ? null : JsonNode.Parse(entry.Output),
                    entry.Error,
                    entry.TraceId))
                .ToList(),
            tasks
                .Select(task => new ProcessInstanceTask(
                    task.Id,
                    task.Step,
                    task.AssigneeKind,
                    task.AssigneeKind == ProcessTasks.UserAssignee ? Name(task.Assignee) : task.Assignee,
                    task.AssigneeKind == ProcessTasks.UserAssignee ? task.Assignee : task.ClaimedBy,
                    task.State,
                    task.Outcome,
                    task.CompletedBy is { } completedBy ? Name(completedBy) : null,
                    task.CompletedBy,
                    task.CreatedAt.UtcDateTime,
                    task.DueAt?.UtcDateTime,
                    task.CompletedAt?.UtcDateTime))
                .ToList(),
            calls
                .Select(call =>
                {
                    if (!mappings.TryGetValue(call.Step, out var fields))
                    {
                        mappings[call.Step] = fields = MappedFields(pinned, pinnedProcess, pinnedSubject, call.Step);
                    }

                    var hidden = call.Input is not null
                        && (!Readable(active, viewer, fields) || Sensitive(pinned, active, fields));
                    var outputs = allowsResolve && call.State == ConnectorCalls.Unknown && row.State == ProcessStarts.Waiting && row.Step == call.Step
                        ? DeclaredOutputs(pinned, call, packages)
                        : null;
                    return new ProcessInstanceCall(
                        call.Id,
                        call.Kind,
                        call.Step,
                        call.Connector,
                        call.Package,
                        call.Version,
                        call.Idempotent,
                        call.Operation,
                        call.State,
                        call.Attempts
                            .Select(attempt => new ProcessCallAttemptEntry(
                                attempt.Kind,
                                attempt.Number,
                                attempt.StartedAt.UtcDateTime,
                                attempt.FinishedAt.UtcDateTime,
                                attempt.Outcome,
                                attempt.HttpStatus,
                                attempt.Error,
                                attempt.TraceId))
                            .ToList(),
                        call.Input is null || hidden ? null : JsonNode.Parse(call.Input),
                        hidden,
                        outputs is not null,
                        outputs);
                })
                .ToList(),
            commands,
            author ? new ProcessStudioAddress(active.Manifest.Name, process.Id) : null);
    }

    // The parameters the instance started with, in declared order, each hidden on its own. A value that a delete trigger or a
    // sub-process step of the pinned release fills from record fields is hidden unless the viewer may read all of those fields
    // and none is sensitive. A parameter that only a person or a schedule fills is a literal and is shown.
    private static async Task<IReadOnlyList<ProcessInstanceValue>> InputAsync(
        NpgsqlConnection connection,
        ApplicationModel pinned,
        ProcessModel pinnedProcess,
        ApplicationModel active,
        InspectionViewer viewer,
        ProcessInstanceRow row,
        CancellationToken cancellationToken)
    {
        var stored = (await InstanceQueries.ValuesAsync(connection, row.Id, cancellationToken)).Parameters is { } text
            ? JsonNode.Parse(text) as JsonObject
            : null;
        return pinnedProcess.Parameters
            .Select(parameter =>
            {
                var hidden = ParameterSources(pinned, pinnedProcess, parameter.Name)
                    .Any(fields => !Readable(active, viewer, fields) || Sensitive(pinned, active, fields));
                var value = stored?.FirstOrDefault(entry => string.Equals(entry.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)).Value;
                return new ProcessInstanceValue(parameter.Name, hidden ? null : value, hidden);
            })
            .ToList();
    }

    // For each delete trigger and each sub-process step of the pinned release that fills the parameter, the record fields its
    // expression reads. A null set is a source whose fields the walker cannot tell, such as a parent's own parameter, so it hides.
    private static List<IReadOnlySet<MappedField>?> ParameterSources(ApplicationModel pinned, ProcessModel process, string parameter)
    {
        var sources = new List<IReadOnlySet<MappedField>?>();
        foreach (var trigger in pinned.Triggers.Where(candidate => candidate.Process.Id == process.Id))
        {
            if (trigger.ParameterExpressions?.FirstOrDefault(entry => string.Equals(entry.Key, parameter, StringComparison.OrdinalIgnoreCase)).Value is { } expression)
            {
                sources.Add(trigger.Entity is { } watched && pinned.FindEntity(watched.Id) is { } entity
                    ? MappingFields.Collect(pinned, entity, expression)
                    : null);
            }
        }

        foreach (var parent in pinned.Processes)
        {
            foreach (var step in parent.Steps.OfType<SubProcessStepModel>().Where(candidate => candidate.ProcessId == process.Id))
            {
                if (step.Parameters.FirstOrDefault(entry => string.Equals(entry.Parameter, parameter, StringComparison.OrdinalIgnoreCase)) is { } given)
                {
                    sources.Add(parent.Entity is { } subject && pinned.FindEntity(subject.Id) is { } entity
                        ? MappingFields.Collect(pinned, entity, given.Value)
                        : null);
                }
            }
        }

        return sources;
    }

    // The end step and the values the instance completed with, as the engine kept them. Each value is hidden when its field is
    // unreadable for the viewer, sensitive in either release, or gone from the active release. Values are null for an instance that
    // completed before the engine kept them.
    private static async Task<ProcessInstanceResult> ResultAsync(
        NpgsqlConnection connection,
        ApplicationModel pinned,
        ApplicationModel active,
        EntityModel? subject,
        InspectionViewer viewer,
        ProcessInstanceRow row,
        CancellationToken cancellationToken)
    {
        if ((await InstanceQueries.ValuesAsync(connection, row.Id, cancellationToken)).Result is not { } text
            || JsonNode.Parse(text) is not JsonObject stored)
        {
            return new ProcessInstanceResult(row.Step, null);
        }

        var values = new List<ProcessInstanceValue>();
        foreach (var (name, value) in stored)
        {
            var hidden = subject is null
                || !Readable(active, viewer, new HashSet<MappedField> { new(subject.Id, name) })
                || Sensitive(pinned, active, new HashSet<MappedField> { new(subject.Id, name) });
            values.Add(new ProcessInstanceValue(name, hidden ? null : value, hidden));
        }

        return new ProcessInstanceResult(row.Step, values);
    }

    // The process in the active release when it exists and the viewer may inspect it, otherwise null.
    private static ProcessModel? InspectableProcess(ApplicationModel application, InspectionViewer viewer, Guid processId) =>
        application.Processes.FirstOrDefault(candidate => candidate.Id == processId) is { } process
        && viewer.MayInspect(application, process)
            ? process
            : null;

    // The outputs the call's operation declares in the pinned release or the installed package
    // version, or null when either is missing, so that a resolve would fail.
    private static List<ProcessCallOutput>? DeclaredOutputs(ApplicationModel pinned, ProcessCallDetail call, PackageCatalog packages)
    {
        if (call.Connector is { } connectorName)
        {
            var operation = pinned.Connectors
                .FirstOrDefault(candidate => string.Equals(candidate.Name, connectorName, StringComparison.OrdinalIgnoreCase))
                ?.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase));
            return operation?.Output.Select(field => new ProcessCallOutput(field.Name, FieldTypes.Name(field.Type), field.Required)).ToList();
        }

        var packageOperation = packages.Find(call.Package!, call.Version!)
            ?.Manifest.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase));
        return packageOperation?.Output.Select(field => new ProcessCallOutput(field.Name, FieldTypes.Name(field.Type), field.Required)).ToList();
    }

    // The fields the step's mapping reads or writes in the pinned release, or null, which hides the
    // input, when the release lacks the step or the step is no call.
    private static IReadOnlySet<MappedField>? MappedFields(
        ApplicationModel pinned, ProcessModel process, EntityModel? subject, string step)
    {
        if (subject is null || !process.TryGetStep(step, out var found))
        {
            return null;
        }

        return found switch
        {
            CallConnectorStepModel call => MappingFields.Collect(pinned, subject, call.Input, call.Output),
            PackageOperationStepModel package => MappingFields.Collect(pinned, subject, package.Input, package.Output),
            _ => null,
        };
    }

    // Readable comes from the fields of the viewer's read grants in the active release. A field or
    // entity that the active release no longer has is not readable.
    private static bool Readable(ApplicationModel active, InspectionViewer viewer, IReadOnlySet<MappedField>? fields)
    {
        if (fields is null)
        {
            return false;
        }

        foreach (var mapped in fields)
        {
            if (active.FindEntity(mapped.EntityId) is not { } entity || !entity.TryGetField(mapped.Field, out var field))
            {
                return false;
            }

            if (viewer.ReadableFields(active, entity) is { } readable && !readable.Contains(field.Name))
            {
                return false;
            }
        }

        return true;
    }

    // A field counts as sensitive when the pinned or the active release marks it, so a release that
    // adds or removes the flag exposes no instance on an older release. A child collection counts
    // when any field of its child entity is sensitive, because the mapping lists child rows only
    // through the owner's collection field.
    private static bool Sensitive(ApplicationModel pinned, ApplicationModel active, IReadOnlySet<MappedField>? fields)
    {
        if (fields is null)
        {
            return true;
        }

        foreach (var mapped in fields)
        {
            foreach (var release in new[] { pinned, active })
            {
                if (release.FindEntity(mapped.EntityId) is not { } entity || !entity.TryGetField(mapped.Field, out var field))
                {
                    continue;
                }

                if (field.Sensitive
                    || (field.Type == FieldType.ChildCollection
                        && release.FindEntity(field.Target!.Id) is { } child
                        && child.Fields.Any(candidate => candidate.Sensitive)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static ProcessGraph Graph(ProcessModel model) =>
        new(model.Start, model.Steps.Select(GraphStep).ToList());

    // A transition's On is text: a decision branch gives its index and the fallback gives "otherwise". A step's label key is its own
    // for every type. A decision branch's and a task outcome's transition gives the label's key, and a decision branch also its name.
    private static ProcessGraphStep GraphStep(ProcessStepModel step) =>
        step switch
        {
            DecisionStepModel decision => new(
                decision.Name,
                "decision",
                step.Label?.TextKey,
                null,
                null,
                decision.Branches
                    .Select((branch, index) => new ProcessGraphTransition(branch.Next, index.ToString(CultureInfo.InvariantCulture), branch.Name, branch.Label?.TextKey, null))
                    .Append(new ProcessGraphTransition(decision.Otherwise, "otherwise", null, null, null))
                    .ToList()),
            TaskStepModel task => new(
                task.Name,
                "task",
                task.Label.TextKey,
                null,
                null,
                task.Outcomes.Select(outcome => new ProcessGraphTransition(outcome.Next, outcome.Name, null, outcome.Label.TextKey, null)).ToList()),
            OperationStepModel operation => Operation(step, operation.Name, operation.Next),
            CallConnectorStepModel call => Operation(step, call.Name, call.Next),
            PackageOperationStepModel package => Operation(step, package.Name, package.Next),
            RaiseEventStepModel raise => Operation(step, raise.Name, raise.Next),
            TimerStepModel timer => new(timer.Name, "timer", step.Label?.TextKey, null, null, [Next(timer.Next)]),
            // An event has no label, so EventLabelKey is null.
            WaitEventStepModel wait => new(
                wait.Name,
                "waitEvent",
                step.Label?.TextKey,
                wait.Event,
                null,
                wait.Timeout is { } timeout
                    ? [Next(wait.Next), new ProcessGraphTransition(timeout.Next, "timeout", null, null, timeout.AfterText)]
                    : [Next(wait.Next)]),
            CreateRecordStepModel create => Operation(step, create.Name, create.Next),
            NotifyStepModel notify => Operation(step, notify.Name, notify.Next),
            SubProcessStepModel sub => Operation(step, sub.Name, sub.Next),
            EndStepModel end => new(end.Name, "end", step.Label?.TextKey, null, null, []),
            _ => throw new InvalidOperationException($"Unexpected step type {step.GetType().Name}."),
        };

    private static ProcessGraphStep Operation(ProcessStepModel step, string name, string next) =>
        new(name, "operation", step.Label?.TextKey, null, null, [Next(next)]);

    private static ProcessGraphTransition Next(string next) =>
        new(next, null, null, null, null);

    private static IResult InstanceNotFound() =>
        NotFound("No instance of this process exists with this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>
/// Who reads an instance detail. A user holds the grants of their roles in the application. The operator of the
/// console stands in for the <c>inspect</c> grant and for a <c>read</c> grant with no <c>fields</c> list, so they
/// inspect every process and read every field. <see cref="Operator"/> also stands in for the <c>retry</c>, <c>cancel</c> and
/// <c>resolve</c> grants, and <see cref="OperatorReadOnly"/> holds none of them, for an instance whose commands cannot run.
/// </summary>
internal sealed class InspectionViewer
{
    private readonly IReadOnlyList<string>? _roles;

    private readonly bool _commands;

    private InspectionViewer(IReadOnlyList<string>? roles, bool commands = false)
    {
        _roles = roles;
        _commands = commands;
    }

    public static InspectionViewer Operator { get; } = new(null, commands: true);

    public static InspectionViewer OperatorReadOnly { get; } = new(null);

    public static InspectionViewer ForUser(IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return new(roles);
    }

    public bool MayInspect(ApplicationModel application, ProcessModel process) =>
        _roles is null || PolicyEvaluator.AllowsInspect(application, _roles, process);

    // The fields of the viewer's read grants on the entity, or null when every field is readable.
    public IReadOnlySet<string>? ReadableFields(ApplicationModel application, EntityModel entity) =>
        _roles is null ? null : PolicyEvaluator.Fields(application, _roles, entity, PolicyAction.Read);

    public bool AllowsRetry(ApplicationModel application, ProcessModel process) =>
        _roles is null ? _commands : PolicyEvaluator.AllowsRetry(application, _roles, process);

    public bool AllowsCancel(ApplicationModel application, ProcessModel process) =>
        _roles is null ? _commands : PolicyEvaluator.AllowsCancel(application, _roles, process);

    public bool AllowsResolve(ApplicationModel application, ProcessModel process) =>
        _roles is null ? _commands : PolicyEvaluator.AllowsResolve(application, _roles, process);
}

/// <summary>One page of the instances of a process, and the number of them on every page.</summary>
internal sealed record ProcessInstanceListResponse(IReadOnlyList<ProcessInstanceListItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// An instance in the list of a process's instances. <paramref name="SubjectLabel"/> is the subject record's display
/// field value, or null without a subject, without a display field, when the user may not read the field or when it is
/// sensitive. <paramref name="ReleaseNumber"/> is the pinned release's number in the release history, or null when it has no row.
/// </summary>
internal sealed record ProcessInstanceListItem(
    Guid Id,
    Guid? SubjectId,
    string? SubjectLabel,
    string State,
    string Step,
    Guid ReleaseId,
    int? ReleaseNumber,
    DateTime CreatedAt,
    DateTime? EndedAt);

/// <summary>
/// One instance of a process, the graph of the release it is pinned to, its path, its tasks and its calls.
/// <paramref name="SubjectEntity"/>, <paramref name="SubjectLabel"/> and <paramref name="ReleaseNumber"/> are as the list gives them.
/// <paramref name="Studio"/> is the address to open the process in Studio for a user who holds the author role, and null for anyone else.
/// <paramref name="Commands"/> lists the operator commands (<c>retry</c>, <c>cancel</c>) that the user holds the grant for and
/// that the instance's state and calls allow. <paramref name="TimerDueAt"/> is the time the timer step that the instance waits on
/// fires, or null when the instance is not waiting on a timer. The detail names the parent and the sub-processes whose process the user may
/// inspect in the active release, with no subject id. <paramref name="Input"/> lists the parameters of an instance of a process without a subject,
/// and is null for a process with one. <paramref name="Result"/> is the end step and the values of a completed instance, and null before.
/// </summary>
internal sealed record ProcessInstanceDetailResponse(
    Guid Id,
    string Process,
    string? SubjectEntity,
    Guid? SubjectId,
    string? SubjectLabel,
    Guid ReleaseId,
    int? ReleaseNumber,
    string State,
    string Step,
    DateTime CreatedAt,
    DateTime? EndedAt,
    DateTime? TimerDueAt,
    IReadOnlyList<ProcessInstanceValue>? Input,
    ProcessInstanceResult? Result,
    ProcessInstanceParent? Parent,
    IReadOnlyList<ProcessSubProcess> SubProcesses,
    ProcessGraph Graph,
    IReadOnlyList<ProcessPathEntry> Path,
    IReadOnlyList<ProcessInstanceTask> Tasks,
    IReadOnlyList<ProcessInstanceCall> Calls,
    IReadOnlyList<string> Commands,
    ProcessStudioAddress? Studio);

/// <summary>
/// A value of an instance's <c>input</c> or <c>result</c>, by name. It is null with <paramref name="Hidden"/> true when the viewer may not
/// see it, under <see href="docs/reference/process-inspection.md#values-and-masking">values and masking</see>.
/// </summary>
internal sealed record ProcessInstanceValue(string Name, JsonNode? Value, bool Hidden);

/// <summary>
/// What a completed instance ended with: the end <paramref name="Step"/> it reached and the subject fields its steps write.
/// <paramref name="Values"/> is null for an instance that completed before the engine kept them.
/// </summary>
internal sealed record ProcessInstanceResult(string Step, IReadOnlyList<ProcessInstanceValue>? Values);

/// <summary>The application and the process resource that open in Studio, for a viewer who holds the author role.</summary>
internal sealed record ProcessStudioAddress(string App, Guid Resource);

/// <summary>The instance that started this one, with the sub-process step in the parent that did so.</summary>
internal sealed record ProcessInstanceParent(Guid Id, string Process, string Step);

/// <summary>An instance that this one started, with the sub-process step in this instance that did so.</summary>
internal sealed record ProcessSubProcess(Guid Id, string Process, string Step, string State, DateTime CreatedAt, DateTime? EndedAt);

/// <summary>The steps of a process, in declaration order, and the name of the first.</summary>
internal sealed record ProcessGraph(string Start, IReadOnlyList<ProcessGraphStep> Steps);

/// <summary>
/// A step of the graph: its type (<c>decision</c>, <c>task</c>, <c>operation</c>, <c>timer</c>, <c>waitEvent</c>, <c>end</c>), the text key of its
/// label or null, where it leads, and for a <c>waitEvent</c> step the name of the event it waits for. <paramref name="EventLabelKey"/>
/// is always null, since an event has no label. <paramref name="Event"/> and <paramref name="EventLabelKey"/> are null for any other step.
/// </summary>
internal sealed record ProcessGraphStep(
    string Name,
    string Type,
    string? LabelKey,
    string? Event,
    string? EventLabelKey,
    IReadOnlyList<ProcessGraphTransition> Transitions);

/// <summary>
/// A transition to the step <paramref name="Next"/>, taken on a decision branch or a task outcome, or null for any other transition.
/// <paramref name="Branch"/> is a decision branch's name, and <paramref name="LabelKey"/> the text key of the label of the branch or the task outcome.
/// <paramref name="After"/> is the timeout's duration as the process file writes it, on a <c>waitEvent</c> step's timeout transition. Each is null where it does not apply.
/// </summary>
internal sealed record ProcessGraphTransition(string Next, string? On, string? Branch, string? LabelKey, string? After);

/// <summary>
/// One occurrence of a step in the instance's history. A failed step has an error and no output.
/// <paramref name="TraceId"/> is the trace id of the run that wrote the entry, or null when it stored none.
/// </summary>
internal sealed record ProcessPathEntry(string Step, DateTime StartedAt, DateTime FinishedAt, string? Decision, JsonNode? Output, string? Error, string? TraceId);

/// <summary>
/// A task of the instance, with who it is assigned to and how it ended. <paramref name="Assignee"/> is a user assignee's name,
/// or its user id when the user has no profile, and a role assignee's role name. <paramref name="AssigneeId"/> is the user
/// assignee's id, or the id of the person who claimed a role task, and null while no one holds it. <paramref name="CompletedBy"/>
/// is the name of the person who completed the task, or their user id when they have no profile.
/// </summary>
internal sealed record ProcessInstanceTask(
    Guid Id,
    string Step,
    string AssigneeKind,
    string Assignee,
    string? AssigneeId,
    string State,
    string? Outcome,
    string? CompletedBy,
    string? CompletedById,
    DateTime CreatedAt,
    DateTime? DueAt,
    DateTime? CompletedAt);

/// <summary>
/// A connector call or package call of the instance, with its attempts and lookups. A connector
/// call has <paramref name="Connector"/> and a package call has <paramref name="Package"/> and
/// <paramref name="Version"/>. <paramref name="Input"/> is null, and <paramref name="InputHidden"/> true,
/// when the user may not read every field the step's mapping uses, or when one of them is sensitive
/// in the pinned or the active release. A package call has <paramref name="Idempotent"/>, as stored when the call was staged.
/// <paramref name="Resolvable"/> is true when the user holds <c>resolve</c>, the call is <c>unknown</c>, the instance
/// is <c>waiting</c> on the call's step, and the pinned release or the installed package version declares the
/// operation. Only then are its declared <paramref name="Outputs"/> listed.
/// </summary>
internal sealed record ProcessInstanceCall(
    Guid Id,
    string Kind,
    string Step,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Connector,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Package,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Idempotent,
    string Operation,
    string State,
    IReadOnlyList<ProcessCallAttemptEntry> Attempts,
    JsonNode? Input,
    bool InputHidden,
    bool Resolvable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ProcessCallOutput>? Outputs);

/// <summary>An output that a call's operation declares, with its field type and whether a resolve must give it.</summary>
internal sealed record ProcessCallOutput(string Name, string Type, bool Required);

/// <summary>
/// An attempt or reconciliation lookup of a call. <c>Kind</c> is <c>attempt</c> or <c>lookup</c>.
/// <paramref name="TraceId"/> is the trace id of the run that wrote the row, or null when it stored none.
/// </summary>
internal sealed record ProcessCallAttemptEntry(
    string Kind,
    int Number,
    DateTime StartedAt,
    DateTime FinishedAt,
    string Outcome,
    int? HttpStatus,
    string? Error,
    string? TraceId);
