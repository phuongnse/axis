using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Processes;

/// <summary>
/// The endpoints for a user who may inspect a process. The list at
/// <c>GET /api/apps/{app}/processes/{process}/instances</c> shows the instances of the process,
/// newest first and paged as the record API pages, whatever release each is pinned to. The
/// <c>state</c> parameter keeps one state, and <c>subject</c> keeps the instances of one record. A
/// request is checked in order: session or service account bearer token (401), application and process (404), the user's
/// <c>inspect</c> grant on the process (403), then the parameters (400), each keyed by its name.
/// An instance is listed only when its subject record exists and matches one of the user's read
/// filters on the subject entity in the active release. The user's <c>read</c> grant on that
/// entity is needed for it, so without one the list is empty. A hidden instance is in neither the
/// page nor the total. A read filter that fails at run time is a 400. A <c>read</c> filter that
/// reads a computed field that is being recomputed is a 409 that names no field or policy, after the
/// parameters and before the instances are read. The list writes no audit record. The problem titles never contain text from the request.
/// <para>
/// The detail at <c>GET /api/apps/{app}/processes/{process}/instances/{id}</c> shows one instance:
/// its properties, the graph of the release it is pinned to, its path (the history rows in order),
/// its tasks, its calls and the operator <c>commands</c> the user may take on it. A call lists its attempts and lookups, and its stored input only when
/// the user may read every field that the step's input and output mapping reads or writes, on any
/// entity, and none of them is sensitive in the pinned or the active release. Otherwise the input is
/// null and marked hidden. The commands come from the user's <c>retry</c> and <c>cancel</c> grants and the states, and a call is
/// <c>resolvable</c> with its declared <c>outputs</c> by the <c>resolve</c> grant, so two users can see one instance differently. The pinned release gives the fields the
/// mapping uses and the active release gives the fields the user may read. The checks run in the order of the list, then the id
/// is parsed. A <c>read</c> filter that reads a computed field that is being recomputed is a 409,
/// as in the list, before any instance is looked up. A malformed id, an unknown id, an instance of another process, and an instance whose
/// subject record the user may not read or that no longer exists are one 404. A read filter that
/// fails at run time for the subject record does not match, so it is a 404 too. The detail writes
/// no audit record.
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

    // The query parameters are bound as strings so every invalid one is reported in one problem.
    private static async Task<IResult> ListAsync(
        string app,
        string process,
        string? state,
        string? subject,
        string? page,
        string? pageSize,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
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

        if (!PolicyEvaluator.AllowsInspect(application, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        PagingQuery.TryRead(page, pageSize, errors, out var pageNumber, out var size);
        if (state is not null && !_states.Contains(state, StringComparer.Ordinal))
        {
            errors["state"] = [$"Must be one of {string.Join(", ", _states)}."];
        }

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

        // No instance of a process without a subject can exist yet.
        if (model.Entity is not { } subjectEntity)
        {
            return Results.Ok(new ProcessInstanceListResponse([], pageNumber, size, 0));
        }

        if (!application.TryGetEntity(subjectEntity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{subjectEntity.Name}' of process '{model.Name}' is not in the model.");
        }

        // An instance whose record the user may not read does not exist for them.
        var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
        if (!read.Granted)
        {
            return Results.Ok(new ProcessInstanceListResponse([], pageNumber, size, 0));
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, null, application, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        var condition = read.Filters is { } filters
            ? RecordFilters.Condition(application, entity, RecordCommand.Expressions(filters), user.Id)
            : null;
        if (await InstanceQueries.ListAsync(
                connection, application.Manifest.Id, model.Id, entity, state, subjectId, condition, pageNumber, size, cancellationToken) is not { } result)
        {
            // PostgreSQL cannot fail one branch of an OR, so every filter of the list is named.
            var logger = loggerFactory.CreateLogger(typeof(ProcessInspectionEndpoints).FullName!);
            foreach (var filter in read.Filters!)
            {
                RecordFilterLog.ListFailed(logger, filter.Policy, filter.Grant, entity.Name);
            }

            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A record filter could not be evaluated for these records.");
        }

        // The list filters by the subject entity, so every instance has a subject.
        var items = result.Items
            .Select(instance => new ProcessInstanceListItem(
                instance.Id,
                instance.SubjectId!.Value,
                instance.State,
                instance.Step,
                instance.ReleaseId,
                instance.CreatedAt.UtcDateTime,
                instance.EndedAt?.UtcDateTime))
            .ToList();
        return Results.Ok(new ProcessInstanceListResponse(items, pageNumber, size, result.TotalCount));
    }

    private static async Task<IResult> GetAsync(
        string app,
        string process,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        PackageCatalog packages,
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

        if (!PolicyEvaluator.AllowsInspect(application, user.Roles, model))
        {
            return Grants.Forbidden();
        }

        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var instanceId))
        {
            return InstanceNotFound();
        }

        // No instance of a process without a subject can exist yet.
        if (model.Entity is not { } subjectEntity)
        {
            return InstanceNotFound();
        }

        if (!application.TryGetEntity(subjectEntity.Name, out var entity))
        {
            throw new InvalidOperationException($"The subject entity '{subjectEntity.Name}' of process '{model.Name}' is not in the model.");
        }

        // An instance whose record the user may not read does not exist for them.
        var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
        if (!read.Granted)
        {
            return InstanceNotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        if (read.Filters is { } pendingFilters
            && await RecordCommand.ReadsPendingAsync(connection, null, application, entity, pendingFilters, loggerFactory, cancellationToken))
        {
            return Grants.PendingFilter();
        }

        if (await InstanceQueries.FindAsync(connection, application.Manifest.Id, model.Id, entity, instanceId, cancellationToken) is not { } row)
        {
            return InstanceNotFound();
        }

        // FindAsync filters by the subject entity, so the instance has a subject.
        var subjectId = row.SubjectId!.Value;
        if (read.Filters is { } filters
            && await RecordCommand.MatchAsync(connection, null, application, entity, subjectId, filters, user.Id, loggerFactory, cancellationToken) is not { Found: true, Matched: true })
        {
            return InstanceNotFound();
        }

        // The graph is the pinned release's, so a newer release does not change a running instance.
        var pinned = await resolver.GetReleaseModelAsync(row.ReleaseId, cancellationToken);
        var graph = Graph(pinned.Processes.Single(candidate => candidate.Id == model.Id));
        var history = await InstanceQueries.HistoryAsync(connection, row.Id, cancellationToken);
        var tasks = await TaskQueries.ListByInstanceAsync(connection, row.Id, cancellationToken);
        var calls = await CallQueries.ListByInstanceAsync(connection, row.Id, cancellationToken);
        var mappings = new Dictionary<string, IReadOnlySet<MappedField>?>(StringComparer.Ordinal);
        var pinnedProcess = pinned.Processes.Single(candidate => candidate.Id == model.Id);
        var pinnedSubject = pinned.FindEntity(entity.Id);
        var allowsResolve = PolicyEvaluator.AllowsResolve(application, user.Roles, model);
        var commands = new List<string>();
        if (row.State == ProcessStarts.Failed && PolicyEvaluator.AllowsRetry(application, user.Roles, model))
        {
            commands.Add("retry");
        }

        if (row.State is ProcessStarts.Running or ProcessStarts.Waiting
            && !calls.Any(call => call.State is ConnectorCalls.Sending or ConnectorCalls.Unknown)
            && PolicyEvaluator.AllowsCancel(application, user.Roles, model))
        {
            commands.Add("cancel");
        }

        return Results.Ok(new ProcessInstanceDetailResponse(
            row.Id,
            model.Name,
            subjectId,
            row.ReleaseId,
            row.State,
            row.Step,
            row.CreatedAt.UtcDateTime,
            row.EndedAt?.UtcDateTime,
            graph,
            history
                .Select(entry => new ProcessPathEntry(
                    entry.Step,
                    entry.StartedAt.UtcDateTime,
                    entry.FinishedAt.UtcDateTime,
                    entry.Decision,
                    entry.Output is null ? null : JsonNode.Parse(entry.Output),
                    entry.Error))
                .ToList(),
            tasks
                .Select(task => new ProcessInstanceTask(
                    task.Id,
                    task.Step,
                    task.AssigneeKind,
                    task.Assignee,
                    task.State,
                    task.Outcome,
                    task.CompletedBy,
                    task.CreatedAt.UtcDateTime,
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
                        && (!Readable(application, user.Roles, fields) || Sensitive(pinned, application, fields));
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
                                attempt.Error))
                            .ToList(),
                        call.Input is null || hidden ? null : JsonNode.Parse(call.Input),
                        hidden,
                        outputs is not null,
                        outputs);
                })
                .ToList(),
            commands));
    }

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

    // Readable comes from the fields of the user's read grants in the active release. A field or
    // entity that the active release no longer has is not readable.
    private static bool Readable(ApplicationModel active, IReadOnlyList<string> roles, IReadOnlySet<MappedField>? fields)
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

            if (PolicyEvaluator.Fields(active, roles, entity, PolicyAction.Read) is { } readable && !readable.Contains(field.Name))
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

    // A transition's On is text: a decision branch gives its index and the fallback gives "otherwise".
    private static ProcessGraphStep GraphStep(ProcessStepModel step) =>
        step switch
        {
            DecisionStepModel decision => new(
                decision.Name,
                "decision",
                null,
                decision.Branches
                    .Select((branch, index) => new ProcessGraphTransition(branch.Next, index.ToString(CultureInfo.InvariantCulture)))
                    .Append(new ProcessGraphTransition(decision.Otherwise, "otherwise"))
                    .ToList()),
            TaskStepModel task => new(
                task.Name,
                "task",
                task.Label.TextKey,
                task.Outcomes.Select(outcome => new ProcessGraphTransition(outcome.Next, outcome.Name)).ToList()),
            OperationStepModel operation => Operation(operation.Name, operation.Next),
            CallConnectorStepModel call => Operation(call.Name, call.Next),
            PackageOperationStepModel package => Operation(package.Name, package.Next),
            RaiseEventStepModel raise => Operation(raise.Name, raise.Next),
            TimerStepModel timer => new(timer.Name, "timer", null, [new ProcessGraphTransition(timer.Next, null)]),
            CreateRecordStepModel create => Operation(create.Name, create.Next),
            NotifyStepModel notify => Operation(notify.Name, notify.Next),
            SubProcessStepModel sub => Operation(sub.Name, sub.Next),
            EndStepModel end => new(end.Name, "end", null, []),
            _ => throw new InvalidOperationException($"Unexpected step type {step.GetType().Name}."),
        };

    private static ProcessGraphStep Operation(string name, string next) =>
        new(name, "operation", null, [new ProcessGraphTransition(next, null)]);

    private static IResult InstanceNotFound() =>
        NotFound("No instance of this process exists with this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}

/// <summary>One page of the instances of a process, and the number of them on every page.</summary>
internal sealed record ProcessInstanceListResponse(IReadOnlyList<ProcessInstanceListItem> Items, int Page, int PageSize, long TotalCount);

/// <summary>An instance in the list of a process's instances.</summary>
internal sealed record ProcessInstanceListItem(
    Guid Id,
    Guid SubjectId,
    string State,
    string Step,
    Guid ReleaseId,
    DateTime CreatedAt,
    DateTime? EndedAt);

/// <summary>
/// One instance of a process, the graph of the release it is pinned to, its path, its tasks and its calls.
/// <paramref name="Commands"/> lists the operator commands (<c>retry</c>, <c>cancel</c>) that the user holds the grant for and
/// that the instance's state and calls allow.
/// </summary>
internal sealed record ProcessInstanceDetailResponse(
    Guid Id,
    string Process,
    Guid SubjectId,
    Guid ReleaseId,
    string State,
    string Step,
    DateTime CreatedAt,
    DateTime? EndedAt,
    ProcessGraph Graph,
    IReadOnlyList<ProcessPathEntry> Path,
    IReadOnlyList<ProcessInstanceTask> Tasks,
    IReadOnlyList<ProcessInstanceCall> Calls,
    IReadOnlyList<string> Commands);

/// <summary>The steps of a process, in declaration order, and the name of the first.</summary>
internal sealed record ProcessGraph(string Start, IReadOnlyList<ProcessGraphStep> Steps);

/// <summary>A step of the graph: its type (<c>decision</c>, <c>task</c>, <c>operation</c> or <c>end</c>), its label for a task, and where it leads.</summary>
internal sealed record ProcessGraphStep(string Name, string Type, string? LabelKey, IReadOnlyList<ProcessGraphTransition> Transitions);

/// <summary>A transition to the step <paramref name="Next"/>, taken on a decision branch or a task outcome, or null for an operation.</summary>
internal sealed record ProcessGraphTransition(string Next, string? On);

/// <summary>One occurrence of a step in the instance's history. A failed step has an error and no output.</summary>
internal sealed record ProcessPathEntry(string Step, DateTime StartedAt, DateTime FinishedAt, string? Decision, JsonNode? Output, string? Error);

/// <summary>A task of the instance, with who it is assigned to and how it ended.</summary>
internal sealed record ProcessInstanceTask(
    Guid Id,
    string Step,
    string AssigneeKind,
    string Assignee,
    string State,
    string? Outcome,
    string? CompletedBy,
    DateTime CreatedAt,
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

/// <summary>An attempt or reconciliation lookup of a call. <c>Kind</c> is <c>attempt</c> or <c>lookup</c>.</summary>
internal sealed record ProcessCallAttemptEntry(
    string Kind,
    int Number,
    DateTime StartedAt,
    DateTime FinishedAt,
    string Outcome,
    int? HttpStatus,
    string? Error);
