using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the processes of an application and builds the checked ones. A process names a loaded
/// entity that is not a child entity. Its step names are unique ignoring letter case, and every
/// transition names one of them. Every step is reachable from <c>start</c>, has a path to an
/// <c>end</c> step and is in no cycle without a waiting step, a <c>task</c> or a <c>timer</c>. The start condition and each decision <c>when</c> parse and
/// type-check as a boolean over the subject entity's fields, computed ones included, its child
/// collections, which only aggregates accept, the named rules, and paths through reference fields
/// of at most 3 hops. An operation step runs <c>updateRecord</c>, <c>callConnector</c>,
/// <c>raiseEvent</c> or a package operation, named <c>&lt;package&gt;/&lt;operation&gt;</c>. Each field an <c>updateRecord</c> <c>set</c> names is a field of the subject
/// entity, named once, that is not computed, numbered by a sequence or a child collection. Each
/// <c>set</c> expression type-checks against its field's type over the same scope as a condition.
/// A <c>callConnector</c> step names a loaded connector and one of its operations. Its inputs are inputs of the operation,
/// named once, that cover every required input, and each input expression type-checks against the
/// input's type over the same scope. Its outputs are outputs of the operation, named once, each
/// written to its own subject field that an <c>updateRecord</c> could set and whose type fits the
/// output's. Its retry <c>backoff</c> is a positive fixed duration. A package operation step names
/// a package the application lists and an operation of the version it pins, and its inputs,
/// outputs and retry are checked the same way against the manifest's fields. A <c>raiseEvent</c> step names
/// a loaded event about the subject entity. Each of its <c>fields</c> keys names a payload field of
/// the event, named once, every payload field has a key, and each expression type-checks against
/// the payload field's type over the same scope. A task step's assignee is exactly one of a <c>user</c>, a
/// <c>text</c> expression over the same scope, and a <c>role</c>, which names a declared role
/// exactly. Its form is a loaded form over the subject entity, its <c>dueIn</c> is a positive fixed
/// duration, its outcome names are unique ignoring letter case, and each outcome's <c>next</c> is a
/// transition. Its <c>deadline</c>'s <c>after</c> is a positive fixed duration too, and its <c>next</c> is a
/// transition that comes after the outcomes. A timer step has exactly one of <c>wait</c>, a positive
/// fixed duration, and <c>until</c>, a <c>date-time</c> expression over the same scope. Every problem of a process is
/// reported in one pass, and a root cause is reported once: a later step with a name already used
/// is left out of the reachability, end and cycle checks, a step with a transition to an
/// unknown step is not also reported as having no path to an end, and a form, entity, role,
/// connector or event file that was not loaded because of its own errors, or a package that failed
/// pinning, is not reported again. A <c>subProcess</c> step names a process of the application.
/// With no subject the child takes every one of its parameters, each named once, and a <c>subject</c>
/// is not allowed. A child with a subject takes a <c>subject</c> expression that gives a record of
/// its entity, which may be left out only when the parent runs for a record of the same entity, and
/// no <c>parameters</c>. The expressions type-check over the same scope as a condition. No process
/// starts itself through <c>subProcess</c> steps, directly or through others.
/// </summary>
internal static class ProcessChecker
{
    private const int DefaultRetryAttempts = 3;

    private static readonly TimeSpan _defaultRetryBackoff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Checks <paramref name="processes"/>, which come in path order. <paramref name="ownerOf"/>
    /// names the owner of a child entity, or gives null for a root entity.
    /// <paramref name="findRole"/> finds a declared role by name, compared exactly.
    /// <paramref name="findConnector"/> finds a loaded connector by name, ignoring letter case, and
    /// <paramref name="findEvent"/> a loaded event. <paramref name="findPackage"/> finds the pinned
    /// package version by id, compared exactly. <paramref name="declaredPackageIds"/> holds the ids
    /// the application lists in <c>packages</c>, or is null when <c>application.json</c> was not
    /// loaded. <paramref name="findProcess"/> finds a process by name, ignoring letter case.
    /// </summary>
    public static void Check(
        IReadOnlyList<ProcessResource> processes,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, FormResource?> findForm,
        IReadOnlySet<string> unloadedFormNames,
        Func<string, RoleResource?> findRole,
        IReadOnlySet<string> unloadedRoleNames,
        Func<string, ConnectorResource?> findConnector,
        IReadOnlySet<string> unloadedConnectorNames,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        Func<string, PinnedOperations?> findPackage,
        IReadOnlySet<string>? declaredPackageIds,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        ReferenceIndexBuilder references,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules,
        List<Diagnostic> diagnostics)
    {
        foreach (var process in processes)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, process.File, path, process.Id));

            if (process.Entity is null)
            {
                CheckWithoutSubject(
                    process, rules, modules, findEntity, findConnector, unloadedConnectorNames, findEvent, unloadedEventNames, findPackage, declaredPackageIds, findProcess, unloadedProcessNames, references, Report, diagnostics);
                continue;
            }

            if (process.Parameters is not null)
            {
                Report(
                    DiagnosticCodes.InvalidProcessSubject,
                    "A process has either an 'entity' or 'parameters', not both. A process with an entity runs for a record of that entity, and a process with parameters has no subject.",
                    "/parameters");
            }

            var entity = findEntity(process.Entity);
            if (entity is not null)
            {
                references.Add(process, "/entity", ReferenceRelations.ProcessEntity, ResourceNode.Of(entity));
            }

            if (entity is null)
            {
                // An entity file that was not loaded because of its own errors is not reported again.
                if (!unloadedEntityNames.Contains(process.Entity))
                {
                    Report(
                        DiagnosticCodes.UnknownProcessEntity,
                        $"The entity '{process.Entity}' was not found. No loaded entity has that name.",
                        "/entity");
                }
            }
            else if (ownerOf(entity.Name) is { } owner)
            {
                // A child row is written only through its owner, so it cannot be a subject record.
                // The expressions are still checked against the entity.
                Report(
                    DiagnosticCodes.ProcessOverChildEntity,
                    $"The entity '{entity.Name}' is a child entity owned by '{owner}'. A process runs for a record of a root entity.",
                    "/entity");
            }

            CheckGraph(process, Report);

            // The expressions cannot be checked without the entity.
            ExpressionScope? scope = null;
            if (entity is not null)
            {
                scope = ExpressionScopes.ForProcess(entity.Name, entity.Fields, rules, findEntity, modules);
                CheckConditions(process, scope, references, Report);
            }

            CheckOperations(
                process, entity, scope, findEntity, findConnector, unloadedConnectorNames, findEvent, unloadedEventNames, findPackage, declaredPackageIds, references, Report);

            CheckSubProcesses(process, entity, scope, findEntity, findProcess, unloadedProcessNames, references, Report);

            CheckTasks(process, entity, scope, findEntity, findForm, unloadedFormNames, findRole, unloadedRoleNames, references, Report);

            CheckTimers(process, scope, references, Report);

            CheckTextKeys(process, references, diagnostics);
        }

        CheckSubProcessCycles(processes, findProcess, diagnostics);
    }

    /// <summary>
    /// Checks a process that has no <c>entity</c>. With no <c>parameters</c> either, it has no
    /// subject to check against, so only the step graph and the text keys are checked. With
    /// <c>parameters</c>, a repeated name, ignoring letter case, is reported once, and the start
    /// condition, decision <c>when</c>, connector or package inputs and sub-process expressions are
    /// checked over the parameters and the named rules. A step that needs a subject record is reported once at the
    /// property to change, and its other properties are not checked.
    /// </summary>
    private static void CheckWithoutSubject(
        ProcessResource process,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules,
        Func<string, EntityResource?> findEntity,
        Func<string, ConnectorResource?> findConnector,
        IReadOnlySet<string> unloadedConnectorNames,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        Func<string, PinnedOperations?> findPackage,
        IReadOnlySet<string>? declaredPackageIds,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        ReferenceIndexBuilder references,
        Action<string, string, string> report,
        List<Diagnostic> diagnostics)
    {
        if (process.Parameters is not { } parameters)
        {
            report(
                DiagnosticCodes.InvalidProcessSubject,
                "A process needs either an 'entity', to run for a record of that entity, or 'parameters', to run without a subject.",
                "");
            CheckGraph(process, report);
            CheckTextKeys(process, references, diagnostics);
            return;
        }

        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            var name = parameters[index].Name;
            if (!firstIndexByName.TryAdd(name, index))
            {
                var firstIndex = firstIndexByName[name];
                report(
                    DiagnosticCodes.DuplicateProcessParameter,
                    $"The parameter name '{name}' is already used by parameter '{parameters[firstIndex].Name}' at '/parameters/{firstIndex}'.",
                    $"/parameters/{index}/name");
            }
        }

        CheckGraph(process, report);

        var scope = ExpressionScopes.ForProcessParameters(parameters, rules, modules);
        CheckConditions(process, scope, references, report);
        CheckSubjectSteps(process, report);

        // A step that needs a subject record is skipped, as CheckSubjectSteps has reported it.
        CheckOperations(
            process, null, scope, _ => null, findConnector, unloadedConnectorNames, findEvent, unloadedEventNames, findPackage, declaredPackageIds, references, report);

        CheckTimers(process, scope, references, report);

        CheckSubProcesses(process, null, scope, findEntity, findProcess, unloadedProcessNames, references, report);

        CheckTextKeys(process, references, diagnostics);
    }

    /// <summary>
    /// Reports each step of a process without a subject that needs a subject record: a task, at its
    /// <c>type</c>, an <c>updateRecord</c> or <c>raiseEvent</c>, at its <c>operation</c>, and a
    /// connector or package call that writes outputs, at its <c>output</c>. An output is written to
    /// a field of the subject, and a task, an update and a raised event are about its record.
    /// </summary>
    private static void CheckSubjectSteps(ProcessResource process, Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            if (step.Type == ProcessStepDefinition.TaskStep)
            {
                report(
                    DiagnosticCodes.StepNeedsSubject,
                    "A task is about a subject record, so a process without a subject cannot have one. Give the process an 'entity' instead of 'parameters', or remove the task.",
                    $"/steps/{index}/type");
                continue;
            }

            if (step.Type != ProcessStepDefinition.OperationStep)
            {
                continue;
            }

            if (string.Equals(step.Operation, ProcessStepDefinition.UpdateRecord, StringComparison.Ordinal)
                || string.Equals(step.Operation, ProcessStepDefinition.RaiseEvent, StringComparison.Ordinal))
            {
                report(
                    DiagnosticCodes.StepNeedsSubject,
                    $"The operation '{step.Operation}' is about a subject record, so a process without a subject cannot use it. Give the process an 'entity' instead of 'parameters', or remove the step.",
                    $"/steps/{index}/operation");
            }
            else if (step.Output is { Count: > 0 })
            {
                report(
                    DiagnosticCodes.StepNeedsSubject,
                    "An output is written to a field of the subject record, so a process without a subject cannot have one. Give the process an 'entity' instead of 'parameters', or remove the output.",
                    $"/steps/{index}/output");
            }
        }
    }

    /// <summary>
    /// Checks each <c>subProcess</c> step against the process it names, found ignoring letter case.
    /// A name that is not found is reported, unless its file was not loaded because of its own
    /// errors, and then the step's <c>subject</c> and <c>parameters</c> are not checked. A child
    /// without a subject takes no <c>subject</c> and every one of its parameters, each named once,
    /// ignoring letter case, with an expression that fits the parameter's type over
    /// <paramref name="scope"/>. A child with a subject takes no <c>parameters</c>. Its
    /// <c>subject</c> is needed when the parent has no subject, or when both entities are known and
    /// differ, and it is an expression over <paramref name="scope"/> that gives a reference to the
    /// child's entity. The expressions are not checked when <paramref name="scope"/> is null, which
    /// is when the subject entity is unknown, and the entities are not compared when either is unknown.
    /// </summary>
    private static void CheckSubProcesses(
        ProcessResource process,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, EntityResource?> findEntity,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            if (step.Type != ProcessStepDefinition.SubProcess)
            {
                continue;
            }

            var path = $"/steps/{index}";
            if (findProcess(step.Process!) is not { } child)
            {
                // A process file that was not loaded because of its own errors is not reported again.
                if (!unloadedProcessNames.Contains(step.Process!))
                {
                    report(
                        DiagnosticCodes.UnknownSubProcess,
                        $"The process '{step.Process}' was not found. The application has no process with that name.",
                        $"{path}/process");
                }

                continue;
            }

            if (child.Entity is null)
            {
                CheckSubProcessParameters(process, step, path, child, scope, references, report);
            }
            else
            {
                CheckSubProcessSubject(process, step, path, child, entity, scope, findEntity, references, report);
            }
        }
    }

    private static void CheckSubProcessParameters(
        ProcessResource process,
        ProcessStepDefinition step,
        string path,
        ProcessResource child,
        ExpressionScope? scope,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (step.Subject is not null)
        {
            report(
                DiagnosticCodes.InvalidSubProcessSubject,
                $"The process '{child.Name}' has no subject, so the step cannot give a 'subject'. Give its parameters in 'parameters' instead.",
                $"{path}/subject");
        }

        var declared = child.Parameters ?? [];
        var givenBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in step.Parameters ?? new Dictionary<string, string>())
        {
            var keyPath = $"{path}/parameters/{key}";
            var parameter = declared.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (parameter is null)
            {
                report(
                    DiagnosticCodes.InvalidSubProcessParameters,
                    $"The parameter '{key}' was not found. The process '{child.Name}' has no parameter with that name.",
                    keyPath);
                continue;
            }

            if (!givenBy.TryAdd(parameter.Name, key))
            {
                report(
                    DiagnosticCodes.InvalidSubProcessParameters,
                    $"The parameter '{parameter.Name}' is already given by '{givenBy[parameter.Name]}' in this step.",
                    keyPath);
                continue;
            }

            // The expression cannot be checked without the subject entity.
            if (scope is null)
            {
                continue;
            }

            var problem = CheckExpression(process, expression, scope, ExpressionScopes.TypeOf(parameter.Type), keyPath, references);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, keyPath);
            }
        }

        foreach (var parameter in declared)
        {
            if (!givenBy.ContainsKey(parameter.Name))
            {
                report(
                    DiagnosticCodes.InvalidSubProcessParameters,
                    $"The parameter '{parameter.Name}' is needed by the process '{child.Name}', but the step does not give it.",
                    $"{path}/parameters");
            }
        }
    }

    private static void CheckSubProcessSubject(
        ProcessResource process,
        ProcessStepDefinition step,
        string path,
        ProcessResource child,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, EntityResource?> findEntity,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (step.Parameters is not null)
        {
            report(
                DiagnosticCodes.InvalidSubProcessParameters,
                $"The process '{child.Name}' has a subject, so the step cannot give 'parameters'. Give the record it runs for in 'subject' instead.",
                $"{path}/parameters");
        }

        var childEntity = findEntity(child.Entity!);
        if (step.Subject is not { } subject)
        {
            if (process.Entity is null)
            {
                report(
                    DiagnosticCodes.InvalidSubProcessSubject,
                    $"The process '{child.Name}' runs for a record of '{child.Entity}', and this process has no subject. Give a 'subject' that names the record.",
                    path);
            }
            else if (entity is not null && childEntity is not null && !ReferenceEquals(entity, childEntity))
            {
                report(
                    DiagnosticCodes.InvalidSubProcessSubject,
                    $"The process '{child.Name}' runs for a record of '{childEntity.Name}', and this process for one of '{entity.Name}'. Give a 'subject' that names the record.",
                    path);
            }

            return;
        }

        // The expression cannot be checked without both entities.
        if (scope is null || childEntity is null)
        {
            return;
        }

        var problem = CheckExpression(process, subject, scope, ExpressionType.Reference(childEntity.Name), $"{path}/subject", references);
        if (problem is not null)
        {
            report(problem.Code, problem.Message, $"{path}/subject");
        }
    }

    /// <summary>
    /// Reports each <c>subProcess</c> step that closes a loop between processes: the process it
    /// starts reaches the step's own process through <c>subProcess</c> steps, or is that process. The
    /// instances would start each other for ever. A step is reported once, at its <c>process</c>,
    /// naming the shortest chain back to its process. Processes are found by name, ignoring letter
    /// case, and a name that is not found has no children.
    /// </summary>
    private static void CheckSubProcessCycles(
        IReadOnlyList<ProcessResource> processes, Func<string, ProcessResource?> findProcess, List<Diagnostic> diagnostics)
    {
        List<(int Index, ProcessResource Child)> Children(ProcessResource process) =>
        [
            .. process.Steps
                .Select((step, index) => (Step: step, Index: index))
                .Where(pair => pair.Step.Type == ProcessStepDefinition.SubProcess)
                .Select(pair => (pair.Index, Child: findProcess(pair.Step.Process!)))
                .Where(pair => pair.Child is not null)
                .Select(pair => (pair.Index, pair.Child!)),
        ];

        foreach (var process in processes)
        {
            foreach (var (index, child) in Children(process))
            {
                // A breadth-first walk from the child gives the shortest way back to the process.
                var previous = new Dictionary<string, ProcessResource>(StringComparer.Ordinal);
                var queue = new Queue<ProcessResource>([child]);
                var visited = new HashSet<string>(StringComparer.Ordinal) { child.File };
                var found = string.Equals(child.File, process.File, StringComparison.Ordinal);
                while (!found && queue.TryDequeue(out var current))
                {
                    foreach (var (_, next) in Children(current))
                    {
                        if (!visited.Add(next.File))
                        {
                            continue;
                        }

                        previous[next.File] = current;
                        if (string.Equals(next.File, process.File, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }

                        queue.Enqueue(next);
                    }
                }

                if (!found)
                {
                    continue;
                }

                // The way from the child to the process's last step, then back to the process.
                var way = new List<ProcessResource>();
                if (!string.Equals(child.File, process.File, StringComparison.Ordinal))
                {
                    for (var member = previous[process.File]; ; member = previous[member.File])
                    {
                        way.Add(member);
                        if (string.Equals(member.File, child.File, StringComparison.Ordinal))
                        {
                            break;
                        }
                    }

                    way.Reverse();
                }

                var chain = new[] { process }.Concat(way).Append(process);

                var names = string.Join(" → ", chain.Select(member => $"'{member.Name}'"));
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.SubProcessCycle,
                    $"Processes {names} start each other through sub-process steps, so they would never end.",
                    process.File,
                    $"/steps/{index}/process",
                    process.Id));
            }
        }
    }

    private static void CheckTextKeys(ProcessResource process, ReferenceIndexBuilder references, List<Diagnostic> diagnostics)
    {
        if (process.StartCondition is { } startCondition)
        {
            ApplicationCompiler.CheckTextKey(startCondition.Message, process, "/startCondition/message", references, diagnostics);
        }

        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            ApplicationCompiler.CheckTextKey(step.Label, process, $"/steps/{index}/label", references, diagnostics);
            var outcomes = step.Outcomes ?? [];
            for (var outcome = 0; outcome < outcomes.Count; outcome++)
            {
                ApplicationCompiler.CheckTextKey(outcomes[outcome].Label, process, $"/steps/{index}/outcomes/{outcome}/label", references, diagnostics);
            }
        }
    }

    /// <summary>
    /// Builds a checked process. Each transition is the declared name of the step it names, and each
    /// condition is compiled over the subject entity's fields, its child collections, the named rules
    /// and paths through reference fields. The fields an operation sets are in the entity's
    /// declaration order, so a release rebuilt from its stored files has the same model. A task's
    /// form is resolved and its <c>dueIn</c> becomes a fixed duration. A connector call holds the
    /// declared names of its connector, its operation, its inputs and outputs and their fields. Its
    /// inputs and outputs are in the operation's declaration order, and its retry policy has the
    /// defaults filled in. A raised event holds the event's declared name, and its payload is in the
    /// event's declaration order. A package operation holds the package id, the pinned version, the
    /// manifest's declared operation name, inputs and outputs in the manifest's order, the retry
    /// policy with the defaults filled in, and the manifest's <c>idempotent</c> and <c>timeout</c>. A
    /// sub-process step holds the child's id and declared name, the compiled <c>subject</c> as a
    /// reference to the child's entity, and the parameters in the child's declaration order.
    /// </summary>
    public static ProcessModel Build(
        ProcessResource process,
        Func<string, EntityResource?> findEntity,
        Func<string, FormResource?> findForm,
        Func<string, ConnectorResource?> findConnector,
        Func<string, EventResource?> findEvent,
        Func<string, PinnedOperations?> findPackage,
        Func<string, ProcessResource?> findProcess,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string> modules)
    {
        // A checked process without a subject has no task, update, raised event or output, so the
        // builders of those use the entity only when there is one.
        var entity = process.Entity is { } subjectName ? findEntity(subjectName)! : null;
        var scope = entity is null
            ? ExpressionScopes.ForProcessParameters(process.Parameters!, rules, modules)
            : ExpressionScopes.ForProcess(entity.Name, entity.Fields, rules, findEntity, modules);

        ExpressionModel Compile(string expression) => ExpressionModel.Compile(expression, scope, ExpressionType.Boolean);

        // A checked process has unique step names, so a transition names exactly one step.
        string Resolve(string name) =>
            process.Steps.First(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase)).Name;

        // A checked set names each field once, ignoring letter case.
        List<RecordAssignmentModel> Assign(IReadOnlyDictionary<string, string> set)
        {
            var values = new Dictionary<string, string>(set, StringComparer.OrdinalIgnoreCase);
            return
            [
                .. entity!.Fields
                    .Where(field => values.ContainsKey(field.Name))
                    .Select(field => new RecordAssignmentModel(
                        field.Name,
                        ExpressionModel.Compile(values[field.Name], scope, ExpressionScopes.TypeOf(field)!))),
            ];
        }

        // A checked timer has exactly one of wait and until, and a valid wait.
        TimerStepModel BuildTimer(ProcessStepDefinition step)
        {
            TimeSpan? wait = step.Wait is { } text && IsoDuration.TryParse(text, out var duration) ? duration : null;
            return new TimerStepModel(
                step.Name,
                wait,
                step.Until is { } until ? ExpressionModel.Compile(until, scope, ExpressionType.DateTime) : null,
                Resolve(step.Next!));
        }

        // A checked task has exactly one of user and role, a loaded form and a valid dueIn.
        TaskStepModel BuildTask(ProcessStepDefinition step)
        {
            var form = findForm(step.Form!)!;
            TimeSpan? dueIn = step.DueIn is { } text && IsoDuration.TryParse(text, out var duration) ? duration : null;
            return new TaskStepModel(
                step.Name,
                step.Label!,
                new TaskAssigneeModel(
                    step.Assignee!.User is { } user ? ExpressionModel.Compile(user, scope, ExpressionType.Text) : null,
                    step.Assignee.Role),
                new FormReference(form.Id, form.Name),
                dueIn,
                step.Outcomes!.Select(outcome => new TaskOutcomeModel(outcome.Name, outcome.Label, Resolve(outcome.Next))).ToList(),
                step.Deadline is { } deadline && IsoDuration.TryParse(deadline.After, out var after)
                    ? new TaskDeadlineModel(after, Resolve(deadline.Next))
                    : null);
        }

        // A checked call names a loaded connector and operation, and each input, output and field once.
        CallConnectorStepModel BuildConnectorCall(ProcessStepDefinition step)
        {
            var connector = findConnector(step.Connector!.Name)!;
            var operation = FindOperation(connector, step.Connector.Operation)!;
            var inputs = new Dictionary<string, string>(step.Input!, StringComparer.OrdinalIgnoreCase);
            var outputs = new Dictionary<string, string>(step.Output ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            TimeSpan? backoff = step.Retry?.Backoff is { } text && IsoDuration.TryParse(text, out var duration) ? duration : null;
            return new CallConnectorStepModel(
                step.Name,
                connector.Name,
                operation.Name,
                [
                    .. operation.Input
                        .Where(input => inputs.ContainsKey(input.Name))
                        .Select(input => new ConnectorInputModel(
                            input.Name,
                            ExpressionModel.Compile(inputs[input.Name], scope, ExpressionScopes.TypeOf(input.Type)))),
                ],
                [
                    .. operation.Output
                        .Where(output => outputs.ContainsKey(output.Name))
                        .Select(output => new ConnectorOutputModel(output.Name, FindField(entity!, outputs[output.Name])!.Name)),
                ],
                new RetryPolicyModel(step.Retry?.Attempts ?? DefaultRetryAttempts, backoff ?? _defaultRetryBackoff),
                Resolve(step.Next!));
        }

        // A checked call names a pinned package and one of its operations, and each input and output once.
        PackageOperationStepModel BuildPackageCall(ProcessStepDefinition step)
        {
            var separator = step.Operation!.IndexOf('/', StringComparison.Ordinal);
            var package = findPackage(step.Operation[..separator])!;
            var operation = FindPackageOperation(package, step.Operation[(separator + 1)..])!;
            var inputs = new Dictionary<string, string>(step.Input!, StringComparer.OrdinalIgnoreCase);
            var outputs = new Dictionary<string, string>(step.Output ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            TimeSpan? backoff = step.Retry?.Backoff is { } text && IsoDuration.TryParse(text, out var duration) ? duration : null;
            return new PackageOperationStepModel(
                step.Name,
                package.Id,
                package.Version,
                operation.Name,
                [
                    .. operation.Input
                        .Where(input => inputs.ContainsKey(input.Name))
                        .Select(input => new ConnectorInputModel(
                            input.Name,
                            ExpressionModel.Compile(inputs[input.Name], scope, ExpressionScopes.TypeOf(input.Type)))),
                ],
                [
                    .. operation.Output
                        .Where(output => outputs.ContainsKey(output.Name))
                        .Select(output => new ConnectorOutputModel(output.Name, FindField(entity!, outputs[output.Name])!.Name)),
                ],
                new RetryPolicyModel(step.Retry?.Attempts ?? DefaultRetryAttempts, backoff ?? _defaultRetryBackoff),
                operation.Idempotent,
                operation.Timeout,
                Resolve(step.Next!),
                operation.Capabilities);
        }

        // A checked step names a process of the application and gives what the process needs: a
        // subject for a child with one, unless it is the parent's own, or each of its parameters.
        SubProcessStepModel BuildSubProcess(ProcessStepDefinition step)
        {
            var child = findProcess(step.Process!)!;
            var values = new Dictionary<string, string>(step.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return new SubProcessStepModel(
                step.Name,
                child.Id,
                child.Name,
                step.Subject is { } subject
                    ? ExpressionModel.Compile(subject, scope, ExpressionType.Reference(findEntity(child.Entity!)!.Name))
                    : null,
                [
                    .. (child.Parameters ?? [])
                        .Where(parameter => values.ContainsKey(parameter.Name))
                        .Select(parameter => new SubProcessParameterModel(
                            parameter.Name,
                            ExpressionModel.Compile(values[parameter.Name], scope, ExpressionScopes.TypeOf(parameter.Type)))),
                ],
                Resolve(step.Next!));
        }

        // A checked raise names a loaded event and gives each of its payload fields once.
        RaiseEventStepModel BuildRaiseEvent(ProcessStepDefinition step)
        {
            var @event = findEvent(step.Event!)!;
            var fields = new Dictionary<string, string>(step.Fields!, StringComparer.OrdinalIgnoreCase);
            return new RaiseEventStepModel(
                step.Name,
                @event.Name,
                [
                    .. @event.Fields.Select(field => new EventPayloadModel(
                        field.Name,
                        ExpressionModel.Compile(fields[field.Name], scope, ExpressionScopes.TypeOf(field.Type)))),
                ],
                Resolve(step.Next!));
        }

        return new ProcessModel
        {
            Id = process.Id,
            Name = process.Name,
            File = process.File,
            Entity = entity is null ? null : new EntityReference(entity.Id, entity.Name),
            Parameters = [.. process.Parameters?.Select(parameter => new ProcessParameterModel(parameter.Name, FieldTypes.Parse(parameter.Type))) ?? []],
            StartCondition = process.StartCondition is { } startCondition
                ? new ProcessStartConditionModel(Compile(startCondition.Expression), startCondition.Message)
                : null,
            Start = Resolve(process.Start),
            Steps = process.Steps
                .Select<ProcessStepDefinition, ProcessStepModel>(step => step.Type switch
                {
                    ProcessStepDefinition.Decision => new DecisionStepModel(
                        step.Name,
                        step.Branches!.Select(branch => new DecisionBranchModel(Compile(branch.When), Resolve(branch.Next))).ToList(),
                        Resolve(step.Otherwise!)),
                    ProcessStepDefinition.TaskStep => BuildTask(step),
                    ProcessStepDefinition.OperationStep when step.Operation == ProcessStepDefinition.CallConnector => BuildConnectorCall(step),
                    ProcessStepDefinition.OperationStep when step.Operation == ProcessStepDefinition.RaiseEvent => BuildRaiseEvent(step),
                    ProcessStepDefinition.OperationStep when step.Operation!.Contains('/', StringComparison.Ordinal) => BuildPackageCall(step),
                    ProcessStepDefinition.OperationStep => new OperationStepModel(
                        step.Name,
                        step.Operation!,
                        Assign(step.Set!),
                        Resolve(step.Next!)),
                    ProcessStepDefinition.Timer => BuildTimer(step),
                    ProcessStepDefinition.SubProcess => BuildSubProcess(step),
                    ProcessStepDefinition.End => new EndStepModel(step.Name),
                    _ => throw new ArgumentOutOfRangeException(nameof(process), step.Type, "The JSON Schema allows only decision, task, operation, subProcess, end and timer steps."),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Checks the step names and the transitions, then walks the steps: breadth-first from
    /// <c>start</c> for reachability, to a fixed point for the path to an end, and depth-first in
    /// file order for cycles. A transition resolves to the first step with its name.
    /// </summary>
    private static void CheckGraph(ProcessResource process, Action<string, string, string> report)
    {
        var steps = process.Steps;
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicate = new bool[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            var name = steps[index].Name;
            if (!firstIndexByName.TryAdd(name, index))
            {
                duplicate[index] = true;
                var firstIndex = firstIndexByName[name];
                report(
                    DiagnosticCodes.DuplicateStepName,
                    $"The step name '{name}' is already used by step '{steps[firstIndex].Name}' at '/steps/{firstIndex}'.",
                    $"/steps/{index}/name");
            }
        }

        // The index of the step a transition names, or null when it names none.
        int? Resolve(string name, string path)
        {
            if (firstIndexByName.TryGetValue(name, out var index))
            {
                return index;
            }

            report(DiagnosticCodes.UnknownStep, $"The step '{name}' was not found. No step of this process has that name.", path);
            return null;
        }

        var start = Resolve(process.Start, "/start");
        var transitions = new List<Transition>[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var path = $"/steps/{index}";
            transitions[index] = [];
            for (var branch = 0; branch < (step.Branches?.Count ?? 0); branch++)
            {
                var branchPath = $"{path}/branches/{branch}/next";
                transitions[index].Add(new Transition(Resolve(step.Branches![branch].Next, branchPath), branchPath));
            }

            if (step.Otherwise is { } otherwise)
            {
                transitions[index].Add(new Transition(Resolve(otherwise, $"{path}/otherwise"), $"{path}/otherwise"));
            }

            if (step.Next is { } next)
            {
                transitions[index].Add(new Transition(Resolve(next, $"{path}/next"), $"{path}/next"));
            }

            for (var outcome = 0; outcome < (step.Outcomes?.Count ?? 0); outcome++)
            {
                var outcomePath = $"{path}/outcomes/{outcome}/next";
                transitions[index].Add(new Transition(Resolve(step.Outcomes![outcome].Next, outcomePath), outcomePath));
            }

            if (step.Deadline is { } deadline)
            {
                var deadlinePath = $"{path}/deadline/next";
                transitions[index].Add(new Transition(Resolve(deadline.Next, deadlinePath), deadlinePath));
            }
        }

        // Every step would be unreachable from an unknown start, which is already reported.
        if (start is { } startIndex)
        {
            CheckReachable(steps, transitions, duplicate, startIndex, report);
        }

        CheckEnds(steps, transitions, duplicate, report);
        CheckCycles(steps, transitions, duplicate, report);
    }

    private static void CheckReachable(
        IReadOnlyList<ProcessStepDefinition> steps, List<Transition>[] transitions, bool[] duplicate, int start, Action<string, string, string> report)
    {
        var reached = new bool[steps.Count];
        reached[start] = true;
        var queue = new Queue<int>([start]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var transition in transitions[current])
            {
                if (transition.Target is { } target && !reached[target])
                {
                    reached[target] = true;
                    queue.Enqueue(target);
                }
            }
        }

        for (var index = 0; index < steps.Count; index++)
        {
            if (!duplicate[index] && !reached[index])
            {
                report(
                    DiagnosticCodes.UnreachableStep,
                    $"The step '{steps[index].Name}' cannot be reached from the start step '{steps[start].Name}'.",
                    $"/steps/{index}");
            }
        }
    }

    /// <summary>
    /// Reports each step with no path to an <c>end</c> step. A step with a transition to an unknown
    /// step counts as having one, because that transition is already reported.
    /// </summary>
    private static void CheckEnds(
        IReadOnlyList<ProcessStepDefinition> steps, List<Transition>[] transitions, bool[] duplicate, Action<string, string, string> report)
    {
        var canEnd = new bool[steps.Count];
        bool changed;
        do
        {
            changed = false;
            for (var index = 0; index < steps.Count; index++)
            {
                if (!canEnd[index]
                    && (steps[index].Type == ProcessStepDefinition.End
                        || transitions[index].Exists(transition => transition.Target is not { } target || canEnd[target])))
                {
                    canEnd[index] = true;
                    changed = true;
                }
            }
        }
        while (changed);

        for (var index = 0; index < steps.Count; index++)
        {
            if (!duplicate[index] && !canEnd[index])
            {
                report(
                    DiagnosticCodes.StepWithoutEnd,
                    $"The step '{steps[index].Name}' has no path to an '{ProcessStepDefinition.End}' step.",
                    $"/steps/{index}");
            }
        }
    }

    /// <summary>
    /// Reports each transition that closes a cycle with no waiting step, at that transition, naming
    /// the steps in the cycle. A <c>task</c> step waits for a person and a <c>timer</c> step waits for
    /// a time, so their transitions are not followed and a loop through one is allowed. The steps are walked depth-first in file order,
    /// each step's transitions in order: the branches, then <c>otherwise</c>, then <c>next</c>, then
    /// the outcomes. A step with two transitions to the same step is reported once.
    /// </summary>
    private static void CheckCycles(
        IReadOnlyList<ProcessStepDefinition> steps, List<Transition>[] transitions, bool[] duplicate, Action<string, string, string> report)
    {
        var onPath = new bool[steps.Count];
        var done = new bool[steps.Count];
        var path = new List<int>();
        var reported = new HashSet<(int From, int To)>();

        void Visit(int index)
        {
            onPath[index] = true;
            path.Add(index);
            foreach (var transition in steps[index].Type is ProcessStepDefinition.TaskStep or ProcessStepDefinition.Timer ? [] : (IEnumerable<Transition>)transitions[index])
            {
                if (transition.Target is not { } target)
                {
                    continue;
                }

                if (onPath[target])
                {
                    if (reported.Add((index, target)))
                    {
                        var members = path.Skip(path.IndexOf(target)).Append(target);
                        var names = string.Join(" → ", members.Select(member => $"'{steps[member].Name}'"));
                        report(DiagnosticCodes.ProcessStepCycle, $"Steps {names} form a cycle with no waiting step, so the process would run for ever.", transition.Path);
                    }
                }
                else if (!done[target])
                {
                    Visit(target);
                }
            }

            path.RemoveAt(path.Count - 1);
            onPath[index] = false;
            done[index] = true;
        }

        for (var index = 0; index < steps.Count; index++)
        {
            if (!duplicate[index] && !done[index])
            {
                Visit(index);
            }
        }
    }

    /// <summary>
    /// Checks the start condition and each decision <c>when</c> as a boolean over
    /// <paramref name="scope"/>. A problem is reported at the expression with its own code.
    /// </summary>
    private static void CheckConditions(
        ProcessResource process, ExpressionScope scope, ReferenceIndexBuilder references, Action<string, string, string> report)
    {
        void CheckCondition(string expression, string path)
        {
            var problem = CheckExpression(process, expression, scope, ExpressionType.Boolean, path, references);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, path);
            }
        }

        if (process.StartCondition is { } startCondition)
        {
            CheckCondition(startCondition.Expression, "/startCondition/expression");
        }

        for (var index = 0; index < process.Steps.Count; index++)
        {
            var branches = process.Steps[index].Branches ?? [];
            for (var branch = 0; branch < branches.Count; branch++)
            {
                CheckCondition(branches[branch].When, $"/steps/{index}/branches/{branch}/when");
            }
        }
    }

    /// <summary>
    /// Parses and type-checks <paramref name="expression"/> against <paramref name="expected"/> and gives its
    /// problem, or null. When it type-checks, the rules it calls are added to <paramref name="references"/> at <paramref name="path"/>.
    /// </summary>
    private static ExpressionDiagnostic? CheckExpression(
        Resource process, string expression, ExpressionScope scope, ExpressionType expected, string path, ReferenceIndexBuilder references)
    {
        var parsed = ExpressionParser.Parse(expression);
        if (!parsed.Succeeded)
        {
            references.RecordExpression(process, path, null, parsed.Diagnostic);
            return parsed.Diagnostic;
        }

        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, expected);
        if (check.Succeeded)
        {
            references.AddRuleCalls(process, path, check);
        }

        references.RecordExpression(process, path, check.Type, check.Diagnostic);
        return check.Diagnostic;
    }

    /// <summary>
    /// Checks each operation step: its operation is <c>updateRecord</c>, <c>callConnector</c>,
    /// <c>raiseEvent</c> or a package operation, which contains a <c>/</c>, matched exactly as a step
    /// <c>type</c> is. A connector call is checked by <see cref="CheckConnectorCall"/>, a package
    /// operation by <see cref="CheckPackageCall"/> and a raised event by <see cref="CheckRaiseEvent"/>. Each <c>set</c> key of an <c>updateRecord</c> names a field
    /// of <paramref name="entity"/>, ignoring letter case, that no earlier key of the step names and
    /// that can be set. Its expression then fits the field's type over <paramref name="scope"/>. The
    /// <c>set</c> is not checked when the subject entity is unknown. The other properties of an
    /// unknown operation are not checked, as their meaning depends on the operation.
    /// </summary>
    private static void CheckOperations(
        ProcessResource process,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, EntityResource?> findEntity,
        Func<string, ConnectorResource?> findConnector,
        IReadOnlySet<string> unloadedConnectorNames,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        Func<string, PinnedOperations?> findPackage,
        IReadOnlySet<string>? declaredPackageIds,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            if (step.Type != ProcessStepDefinition.OperationStep)
            {
                continue;
            }

            if (step.Operation!.Contains('/', StringComparison.Ordinal))
            {
                CheckPackageCall(process, step, index, entity, scope, findPackage, declaredPackageIds, references, report);
                continue;
            }

            if (string.Equals(step.Operation, ProcessStepDefinition.CallConnector, StringComparison.Ordinal))
            {
                CheckConnectorCall(process, step, index, entity, scope, findConnector, unloadedConnectorNames, references, report);
                continue;
            }

            if (string.Equals(step.Operation, ProcessStepDefinition.RaiseEvent, StringComparison.Ordinal))
            {
                // A process without a subject has no record to raise an event about, which is reported already.
                if (process.Entity is not null)
                {
                    CheckRaiseEvent(process, step, index, entity, scope, findEntity, findEvent, unloadedEventNames, references, report);
                }

                continue;
            }

            if (!string.Equals(step.Operation, ProcessStepDefinition.UpdateRecord, StringComparison.Ordinal))
            {
                report(
                    DiagnosticCodes.UnknownOperation,
                    $"The operation '{step.Operation}' was not found. The built-in operations are '{ProcessStepDefinition.UpdateRecord}', '{ProcessStepDefinition.CallConnector}' and '{ProcessStepDefinition.RaiseEvent}'. A package operation is written '<package>/<operation>'.",
                    $"/steps/{index}/operation");
                continue;
            }

            // The fields cannot be checked without the entity.
            if (entity is null || scope is null)
            {
                continue;
            }

            // The key that first set each field, by the field's declared name.
            var setBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, expression) in step.Set ?? new Dictionary<string, string>())
            {
                var path = $"/steps/{index}/set/{key}";
                var field = FindField(entity, key);
                if (field is null)
                {
                    report(
                        DiagnosticCodes.UnknownSetField,
                        $"The field '{key}' was not found. The entity '{entity.Name}' has no field with that name.",
                        path);
                    continue;
                }

                if (!setBy.TryAdd(field.Name, key))
                {
                    report(
                        DiagnosticCodes.UnknownSetField,
                        $"The field '{field.Name}' is already set by '{setBy[field.Name]}' in this step.",
                        path);
                    continue;
                }

                if (ReadOnlyReason(field) is { } reason)
                {
                    report(DiagnosticCodes.ReadOnlySetField, $"The field '{field.Name}' {reason}, so an operation cannot set it.", path);
                    continue;
                }

                var problem = CheckExpression(process, expression, scope, ExpressionScopes.TypeOf(field)!, path, references);
                if (problem is not null)
                {
                    report(problem.Code, problem.Message, path);
                }
            }
        }
    }

    /// <summary>
    /// Checks a <c>callConnector</c> step: its connector is loaded, unless its file was not loaded
    /// because of its own errors, and its operation is an operation of that connector, both
    /// ignoring letter case. Only then are its inputs and outputs checked: each key names an input
    /// or output of the operation, ignoring letter case, that no earlier key names. Every required
    /// input has a key, and each input expression fits the input's type over
    /// <paramref name="scope"/>. Each output is written to a field of <paramref name="entity"/>,
    /// ignoring letter case, that no earlier output writes, that an <c>updateRecord</c> could set
    /// and whose type fits the output's: the same type, or a decimal field for an integer output.
    /// The expressions and fields are not checked when the subject entity is unknown. The retry
    /// <c>backoff</c>, when set, is a positive fixed duration.
    /// </summary>
    private static void CheckConnectorCall(
        ProcessResource process,
        ProcessStepDefinition step,
        int index,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, ConnectorResource?> findConnector,
        IReadOnlySet<string> unloadedConnectorNames,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var path = $"/steps/{index}";
        var reference = step.Connector!;
        if (findConnector(reference.Name) is not { } connector)
        {
            // A connector file that was not loaded because of its own errors is not reported again.
            if (!unloadedConnectorNames.Contains(reference.Name))
            {
                report(
                    DiagnosticCodes.UnknownConnector,
                    $"The connector '{reference.Name}' was not found. No loaded connector has that name.",
                    $"{path}/connector/name");
            }
        }
        else
        {
            // The edge goes to the connector, so it does not depend on the operation.
            references.Add(process, $"{path}/connector/name", ReferenceRelations.ConnectorCall, ResourceNode.Of(connector));
            if (FindOperation(connector, reference.Operation) is not { } operation)
            {
                report(
                    DiagnosticCodes.UnknownConnectorOperation,
                    $"The operation '{reference.Operation}' was not found. The connector '{connector.Name}' has no operation with that name.",
                    $"{path}/connector/operation");
            }
            else
            {
                CheckInputs(process, step, path, operation.Name, ToFields(operation.Input), scope, references, report);
                // An output of a process without a subject is reported already, as it has no field to write.
                if (process.Entity is not null)
                {
                    CheckOutputs(step, path, operation.Name, ToFields(operation.Output), entity, report);
                }
            }
        }

        CheckRetry(step, path, report);
    }

    /// <summary>
    /// Checks a package operation step: its package is pinned, and its operation is an operation of
    /// the manifest of the pinned version, ignoring letter case. A package that is not pinned is
    /// reported only when the application does not list it: a listed package that failed pinning is
    /// already reported, and so is an <c>application.json</c> that was not loaded. Only then are its
    /// inputs and outputs checked like those of a connector call, against the manifest's fields. The
    /// retry <c>backoff</c>, when set, is a positive fixed duration.
    /// </summary>
    private static void CheckPackageCall(
        ProcessResource process,
        ProcessStepDefinition step,
        int index,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, PinnedOperations?> findPackage,
        IReadOnlySet<string>? declaredPackageIds,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var path = $"/steps/{index}";
        var separator = step.Operation!.IndexOf('/', StringComparison.Ordinal);
        var packageId = step.Operation[..separator];
        var operationName = step.Operation[(separator + 1)..];
        if (findPackage(packageId) is not { } package)
        {
            if (declaredPackageIds is not null && !declaredPackageIds.Contains(packageId))
            {
                report(
                    DiagnosticCodes.UnknownStepPackage,
                    $"The package '{packageId}' is not one of the application's packages. Add it to 'packages' in application.json.",
                    $"{path}/operation");
            }
        }
        else
        {
            // An operation is not a node, so the edge goes to the package whatever the operation's name.
            references.Add(process, $"{path}/operation", ReferenceRelations.PackageOperation, new PackageNode(package.Id));
            if (FindPackageOperation(package, operationName) is not { } operation)
            {
                report(
                    DiagnosticCodes.UnknownPackageOperation,
                    $"The operation '{operationName}' was not found. Package '{package.Id}' {package.Version} has no operation with that name.",
                    $"{path}/operation");
            }
            else
            {
                CheckInputs(process, step, path, operation.Name, ToFields(operation.Input), scope, references, report);
                // An output of a process without a subject is reported already, as it has no field to write.
                if (process.Entity is not null)
                {
                    CheckOutputs(step, path, operation.Name, ToFields(operation.Output), entity, report);
                }
            }
        }

        CheckRetry(step, path, report);
    }

    private static void CheckRetry(ProcessStepDefinition step, string path, Action<string, string, string> report)
    {
        if (step.Retry?.Backoff is { } backoff && !IsoDuration.TryParse(backoff, out _))
        {
            report(
                DiagnosticCodes.InvalidRetryBackoff,
                $"The duration '{backoff}' is not valid. 'backoff' is a positive ISO 8601 duration in whole numbers: weeks, such as 'P2W', or days with an optional time part, such as 'P1D', 'PT30S' or 'P1DT12H'. Years and months are not allowed.",
                $"{path}/retry/backoff");
        }
    }

    private static IReadOnlyList<OperationField> ToFields(IReadOnlyList<ConnectorFieldDefinition> fields) =>
        [.. fields.Select(field => new OperationField(field.Name, FieldTypes.Parse(field.Type), field.Required))];

    private static IReadOnlyList<OperationField> ToFields(IReadOnlyList<PackageField> fields) =>
        [.. fields.Select(field => new OperationField(field.Name, field.Type, field.Required))];

    private static void CheckInputs(
        ProcessResource process,
        ProcessStepDefinition step,
        string path,
        string operationName,
        IReadOnlyList<OperationField> fields,
        ExpressionScope? scope,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        // The key that first named each input, by the input's declared name.
        var namedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in step.Input!)
        {
            var keyPath = $"{path}/input/{key}";
            var input = fields.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (input is null)
            {
                report(
                    DiagnosticCodes.UnknownConnectorInput,
                    $"The input '{key}' was not found. The operation '{operationName}' has no input with that name.",
                    keyPath);
                continue;
            }

            if (!namedBy.TryAdd(input.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownConnectorInput,
                    $"The input '{input.Name}' is already given by '{namedBy[input.Name]}' in this step.",
                    keyPath);
                continue;
            }

            // The expression cannot be checked without the entity.
            if (scope is null)
            {
                continue;
            }

            var problem = CheckExpression(process, expression, scope, ExpressionScopes.TypeOf(input.Type), keyPath, references);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, keyPath);
            }
        }

        foreach (var input in fields)
        {
            if (input.Required && !namedBy.ContainsKey(input.Name))
            {
                report(
                    DiagnosticCodes.MissingConnectorInput,
                    $"The input '{input.Name}' is required by the operation '{operationName}', but the step does not give it.",
                    $"{path}/input");
            }
        }
    }

    private static void CheckOutputs(
        ProcessStepDefinition step,
        string path,
        string operationName,
        IReadOnlyList<OperationField> fields,
        EntityResource? entity,
        Action<string, string, string> report)
    {
        // The key that first named each output, by the output's declared name, and the key that
        // first wrote each field, by the field's declared name.
        var namedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var writtenBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, fieldName) in step.Output ?? new Dictionary<string, string>())
        {
            var keyPath = $"{path}/output/{key}";
            var output = fields.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (output is null)
            {
                report(
                    DiagnosticCodes.UnknownConnectorOutput,
                    $"The output '{key}' was not found. The operation '{operationName}' has no output with that name.",
                    keyPath);
                continue;
            }

            if (!namedBy.TryAdd(output.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownConnectorOutput,
                    $"The output '{output.Name}' is already written by '{namedBy[output.Name]}' in this step.",
                    keyPath);
                continue;
            }

            // The field cannot be checked without the entity.
            if (entity is null)
            {
                continue;
            }

            var field = FindField(entity, fieldName);
            if (field is null)
            {
                report(
                    DiagnosticCodes.UnknownOutputField,
                    $"The field '{fieldName}' was not found. The entity '{entity.Name}' has no field with that name.",
                    keyPath);
                continue;
            }

            if (!writtenBy.TryAdd(field.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownOutputField,
                    $"The field '{field.Name}' is already written by the output '{writtenBy[field.Name]}' in this step.",
                    keyPath);
                continue;
            }

            if (ReadOnlyReason(field) is { } reason)
            {
                report(DiagnosticCodes.InvalidOutputField, $"The field '{field.Name}' {reason}, so an output cannot write it.", keyPath);
                continue;
            }

            var outputType = output.Type;
            var fieldType = FieldTypes.Parse(field.Type);
            if (outputType != fieldType && !(outputType == FieldType.Integer && fieldType == FieldType.Decimal))
            {
                report(
                    DiagnosticCodes.InvalidOutputField,
                    $"The output '{output.Name}' is {FieldTypes.Name(outputType)}, so it cannot be written to the field '{field.Name}', which is {field.Type}.",
                    keyPath);
            }
        }
    }

    /// <summary>
    /// Checks a <c>raiseEvent</c> step: its event is loaded, unless its file was not loaded because
    /// of its own errors, ignoring letter case, and the event is about <paramref name="entity"/>.
    /// Only when the event is found are its <c>fields</c> checked: each key names a payload field of
    /// the event, ignoring letter case, that no earlier key names, and each expression fits the
    /// field's type over <paramref name="scope"/>. Every payload field then has a key. The
    /// expressions and the event's entity are not checked when the subject entity is unknown, and
    /// the event's entity is not checked when the event's own entity is unknown.
    /// </summary>
    private static void CheckRaiseEvent(
        ProcessResource process,
        ProcessStepDefinition step,
        int index,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, EntityResource?> findEntity,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        var path = $"/steps/{index}";
        var eventName = step.Event!;
        if (findEvent(eventName) is not { } @event)
        {
            // An event file that was not loaded because of its own errors is not reported again.
            if (!unloadedEventNames.Contains(eventName))
            {
                report(
                    DiagnosticCodes.UnknownStepEvent,
                    $"The event '{eventName}' was not found. No loaded event has that name.",
                    $"{path}/event");
            }

            return;
        }

        references.Add(process, $"{path}/event", ReferenceRelations.RaisedEvent, ResourceNode.Of(@event));

        if (entity is not null && findEntity(@event.Entity) is { } eventEntity && !ReferenceEquals(eventEntity, entity))
        {
            report(
                DiagnosticCodes.StepEventOverOtherEntity,
                $"The event '{@event.Name}' is about the entity '{eventEntity.Name}'. A step can raise only an event about the process's entity '{entity.Name}'.",
                $"{path}/event");
        }

        // The key that first named each payload field, by the field's declared name.
        var namedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in step.Fields!)
        {
            var keyPath = $"{path}/fields/{key}";
            var field = @event.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                report(
                    DiagnosticCodes.UnknownEventPayloadField,
                    $"The field '{key}' was not found. The event '{@event.Name}' has no payload field with that name.",
                    keyPath);
                continue;
            }

            if (!namedBy.TryAdd(field.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownEventPayloadField,
                    $"The field '{field.Name}' is already given by '{namedBy[field.Name]}' in this step.",
                    keyPath);
                continue;
            }

            // The expression cannot be checked without the entity, nor against a field type that
            // the event check already reports.
            if (scope is null || !EventChecker.IsScalar(FieldTypes.Parse(field.Type)))
            {
                continue;
            }

            var problem = CheckExpression(process, expression, scope, ExpressionScopes.TypeOf(field.Type), keyPath, references);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, keyPath);
            }
        }

        foreach (var field in @event.Fields)
        {
            if (!namedBy.ContainsKey(field.Name))
            {
                report(
                    DiagnosticCodes.MissingEventPayloadField,
                    $"The field '{field.Name}' is a payload field of the event '{@event.Name}', but the step does not give it.",
                    $"{path}/fields");
            }
        }
    }

    /// <summary>Finds the first field of <paramref name="entity"/> with <paramref name="name"/>, ignoring letter case.</summary>
    private static FieldDefinition? FindField(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the first operation of <paramref name="connector"/> with <paramref name="name"/>, ignoring letter case.</summary>
    internal static ConnectorOperationDefinition? FindOperation(ConnectorResource connector, string name) =>
        connector.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the first operation of <paramref name="package"/> with <paramref name="name"/>, ignoring letter case.</summary>
    internal static PackageOperation? FindPackageOperation(PinnedOperations package, string name) =>
        package.Operations.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a process cannot write <paramref name="field"/>, or null when it can.</summary>
    private static string? ReadOnlyReason(FieldDefinition field) =>
        field.Expression is not null ? "is computed"
        : field.Sequence is not null ? "is numbered by a sequence"
        : FieldTypes.Parse(field.Type) == FieldType.ChildCollection ? "is a child collection"
        : FieldTypes.Parse(field.Type) == FieldType.References ? "is a references field"
        : null;

    /// <summary>
    /// Checks each timer step: it has exactly one of <c>wait</c> and <c>until</c>. A <c>wait</c> is a
    /// positive fixed duration, as a task's <c>dueIn</c> is. An <c>until</c> type-checks as a
    /// <c>date-time</c> over <paramref name="scope"/>, and is not checked when the subject entity is
    /// unknown.
    /// </summary>
    private static void CheckTimers(
        ProcessResource process, ExpressionScope? scope, ReferenceIndexBuilder references, Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            if (step.Type != ProcessStepDefinition.Timer)
            {
                continue;
            }

            var path = $"/steps/{index}";
            if ((step.Wait is null) == (step.Until is null))
            {
                report(DiagnosticCodes.InvalidTimerStep, "A timer step must have exactly one of 'wait' and 'until'.", path);
            }

            if (step.Wait is { } wait && !IsoDuration.TryParse(wait, out _))
            {
                report(
                    DiagnosticCodes.InvalidTaskDueIn,
                    $"The duration '{wait}' is not valid. 'wait' is a positive ISO 8601 duration in whole numbers: weeks, such as 'P2W', or days with an optional time part, such as 'P3D', 'PT4H' or 'P1DT12H'. Years and months are not allowed.",
                    $"{path}/wait");
            }

            if (step.Until is { } until && scope is not null)
            {
                var problem = CheckExpression(process, until, scope, ExpressionType.DateTime, $"{path}/until", references);
                if (problem is not null)
                {
                    report(problem.Code, problem.Message, $"{path}/until");
                }
            }
        }
    }

    /// <summary>
    /// Checks each task step: its assignee has exactly one of <c>user</c> and <c>role</c>, a
    /// <c>user</c> expression gives <c>text</c> over <paramref name="scope"/>, and a <c>role</c> is
    /// a role <paramref name="findRole"/> finds, compared exactly, unless its file was not loaded. Its
    /// form is a loaded form whose entity is <paramref name="entity"/>. Its <c>dueIn</c>, when set, is a positive
    /// fixed duration, and its outcome names are unique ignoring letter case. The expression and the
    /// form's entity are not checked when the subject entity is unknown, and the form's entity is not
    /// checked when the form's own entity is unknown.
    /// </summary>
    private static void CheckTasks(
        ProcessResource process,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, EntityResource?> findEntity,
        Func<string, FormResource?> findForm,
        IReadOnlySet<string> unloadedFormNames,
        Func<string, RoleResource?> findRole,
        IReadOnlySet<string> unloadedRoleNames,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];

            // A task in a process without a subject is reported already, and its other properties are not checked.
            if (step.Type != ProcessStepDefinition.TaskStep || process.Entity is null)
            {
                continue;
            }

            var path = $"/steps/{index}";
            var assignee = step.Assignee!;
            if ((assignee.User is null) == (assignee.Role is null))
            {
                report(DiagnosticCodes.InvalidTaskAssignee, "A task's assignee must have exactly one of 'user' and 'role'.", $"{path}/assignee");
            }

            // A role file that was not loaded because of its own errors is not reported again.
            if (assignee.Role is { } role)
            {
                if (findRole(role) is { } roleResource)
                {
                    references.Add(process, $"{path}/assignee/role", ReferenceRelations.TaskRole, ResourceNode.Of(roleResource));
                }
                else if (!unloadedRoleNames.Contains(role))
                {
                    report(DiagnosticCodes.UnknownTaskRole, $"The role '{role}' was not found. No declared role has that name.", $"{path}/assignee/role");
                }
            }

            if (assignee.User is { } user && scope is not null)
            {
                var problem = CheckExpression(process, user, scope, ExpressionType.Text, $"{path}/assignee/user", references);
                if (problem is not null)
                {
                    report(problem.Code, problem.Message, $"{path}/assignee/user");
                }
            }

            var formName = step.Form!;
            if (findForm(formName) is { } form)
            {
                references.Add(process, $"{path}/form", ReferenceRelations.TaskForm, ResourceNode.Of(form));
                if (entity is not null && findEntity(form.Entity) is { } formEntity && !ReferenceEquals(formEntity, entity))
                {
                    report(
                        DiagnosticCodes.TaskFormOverOtherEntity,
                        $"The form '{form.Name}' is over the entity '{formEntity.Name}'. A task's form must be over the process's entity '{entity.Name}'.",
                        $"{path}/form");
                }
            }
            else if (!unloadedFormNames.Contains(formName))
            {
                report(DiagnosticCodes.UnknownTaskForm, $"The form '{formName}' was not found. No loaded form has that name.", $"{path}/form");
            }

            if (step.DueIn is { } dueIn && !IsoDuration.TryParse(dueIn, out _))
            {
                report(
                    DiagnosticCodes.InvalidTaskDueIn,
                    $"The duration '{dueIn}' is not valid. 'dueIn' is a positive ISO 8601 duration in whole numbers: weeks, such as 'P2W', or days with an optional time part, such as 'P3D', 'PT4H' or 'P1DT12H'. Years and months are not allowed.",
                    $"{path}/dueIn");
            }

            if (step.Deadline is { } deadline && !IsoDuration.TryParse(deadline.After, out _))
            {
                report(
                    DiagnosticCodes.InvalidTaskDueIn,
                    $"The duration '{deadline.After}' is not valid. 'deadline.after' is a positive ISO 8601 duration in whole numbers: weeks, such as 'P2W', or days with an optional time part, such as 'P3D', 'PT4H' or 'P1DT12H'. Years and months are not allowed.",
                    $"{path}/deadline/after");
            }

            var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var outcomes = step.Outcomes!;
            for (var outcome = 0; outcome < outcomes.Count; outcome++)
            {
                var name = outcomes[outcome].Name;
                if (!firstIndexByName.TryAdd(name, outcome))
                {
                    var firstIndex = firstIndexByName[name];
                    report(
                        DiagnosticCodes.DuplicateOutcomeName,
                        $"The outcome name '{name}' is already used by outcome '{outcomes[firstIndex].Name}' at '{path}/outcomes/{firstIndex}'.",
                        $"{path}/outcomes/{outcome}/name");
                }
            }
        }
    }

    /// <summary>An input or output field of a connector or package operation, with its type as a field type.</summary>
    private sealed record OperationField(string Name, FieldType Type, bool Required);

    /// <summary>A transition and its path in the process file. <see cref="Target"/> is the index of the step it names, or null when it names none.</summary>
    private readonly record struct Transition(int? Target, string Path);
}

/// <summary>The pinned version of an extension package: its id, its <c>major.minor.patch</c> version and the operations its manifest offers.</summary>
internal sealed record PinnedOperations(string Id, string Version, IReadOnlyList<PackageOperation> Operations);
