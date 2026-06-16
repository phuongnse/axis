namespace Axis.WorkflowEngine.Domain.ReadModels;

/// <summary>
/// Local read model for WorkflowEngine: stores the full workflow structure (steps + transitions)
/// captured at publish time. Never queries WorkflowBuilder's database — maintained via
/// WorkflowPublished domain events from WorkflowBuilder.
/// </summary>
public sealed class WorkflowSnapshot
{
    public Guid WorkflowId { get; private set; }
    public Guid tenantId { get; private set; }
    public IReadOnlyList<StepDefinitionSnapshot> Steps { get; private set; } = [];
    public IReadOnlyList<TransitionSnapshot> Transitions { get; private set; } = [];

    private WorkflowSnapshot() { } // EF Core materialisation

    public static WorkflowSnapshot Create(
        Guid workflowId,
        Guid tenantId,
        IReadOnlyList<StepDefinitionSnapshot> steps,
        IReadOnlyList<TransitionSnapshot> transitions)
        => new()
        {
            WorkflowId = workflowId,
            tenantId = tenantId,
            Steps = steps,
            Transitions = transitions
        };

    public void Update(
        IReadOnlyList<StepDefinitionSnapshot> steps,
        IReadOnlyList<TransitionSnapshot> transitions)
    {
        Steps = steps;
        Transitions = transitions;
    }

    // ─── Graph helpers used by the engine ────────────────────────────────────

    /// <summary>Returns all step IDs reachable from <paramref name="startStepId"/> via transitions.</summary>
    public IReadOnlySet<Guid> GetReachableStepIds(Guid startStepId)
    {
        HashSet<Guid> visited = [];
        Queue<Guid> queue = new();
        queue.Enqueue(startStepId);

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (TransitionSnapshot t in Transitions.Where(t => t.FromStepId == current))
                queue.Enqueue(t.ToStepId);
        }

        return visited;
    }

    /// <summary>Returns transition destinations from <paramref name="stepId"/>.</summary>
    public IReadOnlyList<Guid> GetNextStepIds(Guid stepId)
        => Transitions.Where(t => t.FromStepId == stepId).Select(t => t.ToStepId).ToList();
}
