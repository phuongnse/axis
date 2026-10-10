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
/// of at most 3 hops. Every problem of a process is
/// reported in one pass, and a root cause is reported once: a later step with a name already used
/// is left out of the reachability, end and cycle checks, and a step with a transition to an
/// unknown step is not also reported as having no path to an end.
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
            if (entity is not null)
            {
                CheckConditions(process, ExpressionScopes.ForProcess(entity.Fields, rules, findEntity), Report);
            }

            if (process.StartCondition is { } startCondition)
            {
                ApplicationCompiler.CheckTextKey(startCondition.Message, process.File, process.Id, "/startCondition/message", textKeys, diagnostics);
            }
        }
    }

    /// <summary>
    /// Builds a checked process. Each transition is the declared name of the step it names, and each
    /// condition is compiled over the subject entity's fields, its child collections, the named rules
    /// and paths through reference fields.
    /// </summary>
    public static ProcessModel Build(ProcessResource process, Func<string, EntityResource?> findEntity, IEnumerable<ExpressionRule> rules)
    {
        var entity = findEntity(process.Entity)!;
        var scope = ExpressionScopes.ForProcess(entity.Fields, rules, findEntity);

        ExpressionModel Compile(string expression) => ExpressionModel.Compile(expression, scope, ExpressionType.Boolean);

        // A checked process has unique step names, so a transition names exactly one step.
        string Resolve(string name) =>
            process.Steps.First(step => string.Equals(step.Name, name, StringComparison.OrdinalIgnoreCase)).Name;

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
                    ProcessStepDefinition.End => new EndStepModel(step.Name),
                    _ => throw new ArgumentOutOfRangeException(nameof(process), step.Type, "The JSON Schema allows only decision and end steps."),
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
    /// branches, then <c>otherwise</c>. A step with two transitions to the same step is reported once.
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

    /// <summary>A transition and its path in the process file. <see cref="Target"/> is the index of the step it names, or null when it names none.</summary>
    private readonly record struct Transition(int? Target, string Path);
}
