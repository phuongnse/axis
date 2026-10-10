using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the processes of an application and builds the checked ones. A process names a loaded
/// entity that is not a child entity. Its step names are unique ignoring letter case, and every
/// transition names one of them. Every step is reachable from <c>start</c>, has a path to an
/// <c>end</c> step and is in no cycle. The start condition and each decision <c>when</c> parse and
/// type-check as a boolean over the subject entity's fields, computed ones included, its child
/// collections, which only aggregates accept, the named rules, and paths through reference fields
/// of at most 3 hops. An operation step runs <c>updateRecord</c>, and each field its <c>set</c>
/// names is a field of the subject entity, named once, that is not computed, numbered by a
/// sequence or a child collection. Each <c>set</c> expression type-checks against its field's type
/// over the same scope as a condition. A task step's assignee is exactly one of a <c>user</c>, a
/// <c>text</c> expression over the same scope, and a <c>role</c>. Its form is a loaded form over
/// the subject entity, its <c>dueIn</c> is a positive fixed duration, its outcome names are unique
/// ignoring letter case, and each outcome's <c>next</c> is a transition. Every problem of a process is
/// reported in one pass, and a root cause is reported once: a later step with a name already used
/// is left out of the reachability, end and cycle checks, a step with a transition to an
/// unknown step is not also reported as having no path to an end, and a form or entity file that
/// was not loaded because of its own errors is not reported again.
/// </summary>
internal static class ProcessChecker
{
    /// <summary>
    /// Checks <paramref name="processes"/>, which come in path order. <paramref name="ownerOf"/>
    /// names the owner of a child entity, or gives null for a root entity.
    /// </summary>
    public static void Check(
        IReadOnlyList<ProcessResource> processes,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, FormResource?> findForm,
        IReadOnlySet<string> unloadedFormNames,
        IReadOnlySet<string> textKeys,
        IEnumerable<ExpressionRule> rules,
        List<Diagnostic> diagnostics)
    {
        foreach (var process in processes)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, process.File, path, process.Id));

            var entity = findEntity(process.Entity);
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
                scope = ExpressionScopes.ForProcess(entity.Fields, rules, findEntity);
                CheckConditions(process, scope, Report);
                CheckOperations(process, entity, scope, Report);
            }

            CheckTasks(process, entity, scope, findEntity, findForm, unloadedFormNames, Report);

            if (process.StartCondition is { } startCondition)
            {
                ApplicationCompiler.CheckTextKey(startCondition.Message, process.File, process.Id, "/startCondition/message", textKeys, diagnostics);
            }

            for (var index = 0; index < process.Steps.Count; index++)
            {
                var step = process.Steps[index];
                ApplicationCompiler.CheckTextKey(step.Label, process.File, process.Id, $"/steps/{index}/label", textKeys, diagnostics);
                var outcomes = step.Outcomes ?? [];
                for (var outcome = 0; outcome < outcomes.Count; outcome++)
                {
                    ApplicationCompiler.CheckTextKey(
                        outcomes[outcome].Label, process.File, process.Id, $"/steps/{index}/outcomes/{outcome}/label", textKeys, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Builds a checked process. Each transition is the declared name of the step it names, and each
    /// condition is compiled over the subject entity's fields, its child collections, the named rules
    /// and paths through reference fields. The fields an operation sets are in the entity's
    /// declaration order, so a release rebuilt from its stored files has the same model. A task's
    /// form is resolved and its <c>dueIn</c> becomes a fixed duration.
    /// </summary>
    public static ProcessModel Build(
        ProcessResource process, Func<string, EntityResource?> findEntity, Func<string, FormResource?> findForm, IEnumerable<ExpressionRule> rules)
    {
        var entity = findEntity(process.Entity)!;
        var scope = ExpressionScopes.ForProcess(entity.Fields, rules, findEntity);

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
                .. entity.Fields
                    .Where(field => values.ContainsKey(field.Name))
                    .Select(field => new RecordAssignmentModel(
                        field.Name,
                        ExpressionModel.Compile(values[field.Name], scope, ExpressionScopes.TypeOf(field)!))),
            ];
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
                step.Outcomes!.Select(outcome => new TaskOutcomeModel(outcome.Name, outcome.Label, Resolve(outcome.Next))).ToList());
        }

        return new ProcessModel
        {
            Id = process.Id,
            Name = process.Name,
            File = process.File,
            Entity = new EntityReference(entity.Id, entity.Name),
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
                    ProcessStepDefinition.OperationStep => new OperationStepModel(
                        step.Name,
                        step.Operation!,
                        Assign(step.Set!),
                        Resolve(step.Next!)),
                    ProcessStepDefinition.End => new EndStepModel(step.Name),
                    _ => throw new ArgumentOutOfRangeException(nameof(process), step.Type, "The JSON Schema allows only decision, task, operation and end steps."),
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
    /// Reports each transition that closes a cycle, at that transition, naming the steps in the
    /// cycle. The steps are walked depth-first in file order, each step's transitions in order: the
    /// branches, then <c>otherwise</c>, then <c>next</c>, then the outcomes. A step with two transitions to the same step is reported once.
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
            foreach (var transition in transitions[index])
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
                        report(DiagnosticCodes.ProcessStepCycle, $"Steps {names} form a cycle. A process may not loop.", transition.Path);
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
    private static void CheckConditions(ProcessResource process, ExpressionScope scope, Action<string, string, string> report)
    {
        void CheckCondition(string expression, string path)
        {
            var parsed = ExpressionParser.Parse(expression);
            ExpressionDiagnostic? problem = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean).Diagnostic
                : parsed.Diagnostic;
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
    /// Checks each operation step: its operation is <c>updateRecord</c>, matched exactly as a step
    /// <c>type</c> is, and each <c>set</c> key names a field of <paramref name="entity"/>, ignoring
    /// letter case, that no earlier key of the step names and that can be set. Its expression then
    /// fits the field's type over <paramref name="scope"/>. The <c>set</c> of an unknown operation
    /// is not checked, as its meaning depends on the operation.
    /// </summary>
    private static void CheckOperations(ProcessResource process, EntityResource entity, ExpressionScope scope, Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            if (step.Type != ProcessStepDefinition.OperationStep)
            {
                continue;
            }

            if (!string.Equals(step.Operation, ProcessStepDefinition.UpdateRecord, StringComparison.Ordinal))
            {
                report(
                    DiagnosticCodes.UnknownOperation,
                    $"The operation '{step.Operation}' was not found. The built-in operation is '{ProcessStepDefinition.UpdateRecord}'.",
                    $"/steps/{index}/operation");
                continue;
            }

            // The key that first set each field, by the field's declared name.
            var setBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, expression) in step.Set ?? new Dictionary<string, string>())
            {
                var path = $"/steps/{index}/set/{key}";
                var field = entity.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
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

                var reason = field.Expression is not null ? "is computed"
                    : field.Sequence is not null ? "is numbered by a sequence"
                    : FieldTypes.Parse(field.Type) == FieldType.ChildCollection ? "is a child collection"
                    : null;
                if (reason is not null)
                {
                    report(DiagnosticCodes.ReadOnlySetField, $"The field '{field.Name}' {reason}, so an operation cannot set it.", path);
                    continue;
                }

                var parsed = ExpressionParser.Parse(expression);
                ExpressionDiagnostic? problem = parsed.Succeeded
                    ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionScopes.TypeOf(field)!).Diagnostic
                    : parsed.Diagnostic;
                if (problem is not null)
                {
                    report(problem.Code, problem.Message, path);
                }
            }
        }
    }

    /// <summary>
    /// Checks each task step: its assignee has exactly one of <c>user</c> and <c>role</c>, and a
    /// <c>user</c> expression gives <c>text</c> over <paramref name="scope"/>. Its form is a loaded
    /// form whose entity is <paramref name="entity"/>. Its <c>dueIn</c>, when set, is a positive
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
        Action<string, string, string> report)
    {
        for (var index = 0; index < process.Steps.Count; index++)
        {
            var step = process.Steps[index];
            if (step.Type != ProcessStepDefinition.TaskStep)
            {
                continue;
            }

            var path = $"/steps/{index}";
            var assignee = step.Assignee!;
            if ((assignee.User is null) == (assignee.Role is null))
            {
                report(DiagnosticCodes.InvalidTaskAssignee, "A task's assignee must have exactly one of 'user' and 'role'.", $"{path}/assignee");
            }

            if (assignee.User is { } user && scope is not null)
            {
                var parsed = ExpressionParser.Parse(user);
                ExpressionDiagnostic? problem = parsed.Succeeded
                    ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Text).Diagnostic
                    : parsed.Diagnostic;
                if (problem is not null)
                {
                    report(problem.Code, problem.Message, $"{path}/assignee/user");
                }
            }

            var formName = step.Form!;
            if (findForm(formName) is { } form)
            {
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

    /// <summary>A transition and its path in the process file. <see cref="Target"/> is the index of the step it names, or null when it names none.</summary>
    private readonly record struct Transition(int? Target, string Path);
}
