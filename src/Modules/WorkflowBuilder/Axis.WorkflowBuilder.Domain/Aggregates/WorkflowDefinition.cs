using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowBuilder.Domain.ValueObjects;

namespace Axis.WorkflowBuilder.Domain.Aggregates;

public sealed class WorkflowDefinition : AggregateRoot<Guid>
{
    private readonly List<WorkflowStep> _steps = [];
    private readonly List<StepTransition> _transitions = [];
    private readonly List<WorkflowTrigger> _triggers = [];

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public WorkflowStatus Status { get; private set; }
    public Guid OrganizationId { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; }

    public IReadOnlyList<WorkflowStep> Steps => _steps.AsReadOnly();
    public IReadOnlyList<StepTransition> Transitions => _transitions.AsReadOnly();
    public IReadOnlyList<WorkflowTrigger> Triggers => _triggers.AsReadOnly();

    private WorkflowDefinition() : base(default) { Name = null!; CreatedBy = string.Empty; } // EF Core materialisation

    private WorkflowDefinition(Guid id, string name, string? description,
        Guid organizationId, string createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Description = description;
        Status = WorkflowStatus.Draft;
        OrganizationId = organizationId;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static WorkflowDefinition Create(string name, string? description, Guid organizationId, string createdBy)
    {
        ValidateName(name);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        WorkflowDefinition wf = new(Guid.NewGuid(), name.Trim(), description?.Trim(), organizationId, createdBy, now);

        // US-047: new workflow starts with a Start and End node
        wf._steps.Add(WorkflowStep.Create("Start", StepType.Start, null));
        wf._steps.Add(WorkflowStep.Create("End", StepType.End, null));

        wf.RaiseDomainEvent(new WorkflowCreated(wf.Id, organizationId, wf.Name));
        return wf;
    }

    public void Update(string name, string? description)
    {
        ValidateName(name);
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public WorkflowStep AddStep(string name, StepType type, IReadOnlyDictionary<string, object?>? config)
    {
        // Start and End are auto-managed; user cannot add them directly
        if (type == StepType.Start || type == StepType.End)
            throw new InvalidOperationException("Start and End step types are reserved and cannot be added manually.");

        WorkflowStep step = WorkflowStep.Create(name, type, config);
        _steps.Add(step);
        UpdatedAt = DateTimeOffset.UtcNow;
        return step;
    }

    public void RemoveStep(Guid stepId)
    {
        WorkflowStep step = _steps.SingleOrDefault(s => s.Id == stepId)
            ?? throw new InvalidOperationException("Step not found.");

        if (step.Type == StepType.Start || step.Type == StepType.End)
            throw new InvalidOperationException("Cannot remove the Start or End step.");

        _steps.Remove(step);
        // Remove any transitions involving this step
        _transitions.RemoveAll(t => t.FromStepId == stepId || t.ToStepId == stepId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddTransition(Guid fromStepId, Guid toStepId, string? label)
    {
        EnsureStepExists(fromStepId);
        EnsureStepExists(toStepId);

        // Cycle detection — simple reachability: can toStepId reach fromStepId already?
        if (CanReach(toStepId, fromStepId))
            throw new InvalidOperationException(
                "Adding this transition would create a cycle in the workflow, which is not allowed.");

        _transitions.Add(new StepTransition(fromStepId, toStepId, label));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveTransition(Guid fromStepId, Guid toStepId)
    {
        StepTransition? transition = _transitions.FirstOrDefault(t => t.FromStepId == fromStepId && t.ToStepId == toStepId);
        if (transition is not null)
        {
            _transitions.Remove(transition);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void ConfigureStep(Guid stepId, string name, IReadOnlyDictionary<string, object?>? config)
    {
        WorkflowStep step = _steps.SingleOrDefault(s => s.Id == stepId)
            ?? throw new InvalidOperationException("Step not found.");

        if (step.Type is StepType.Start or StepType.End)
            throw new InvalidOperationException($"Cannot configure a reserved {step.Type} step.");

        step.UpdateConfig(name, config);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AddTrigger(TriggerType type, IReadOnlyDictionary<string, object?>? config)
    {
        if (_triggers.Any(t => t.Type == type))
            throw new InvalidOperationException($"A {type} trigger is already configured on this workflow.");

        _triggers.Add(new WorkflowTrigger(type, config));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RemoveTrigger(TriggerType type)
    {
        WorkflowTrigger? trigger = _triggers.FirstOrDefault(t => t.Type == type);
        if (trigger is not null)
        {
            _triggers.Remove(trigger);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>US-049: Validates and transitions to Active status.</summary>
    public void Publish()
    {
        if (Status == WorkflowStatus.Active)
            throw new InvalidOperationException("Workflow is already active.");

        // Must have at least one trigger
        if (_triggers.Count == 0)
            throw new InvalidOperationException(
                "Workflow must have at least one trigger before it can be published.");

        // Must have at least one step beyond Start/End
        List<WorkflowStep> nonTerminalSteps = _steps.Where(s => s.Type != StepType.Start && s.Type != StepType.End).ToList();
        if (nonTerminalSteps.Count == 0)
            throw new InvalidOperationException(
                "Workflow must have at least one step beyond the Start and End nodes.");

        Status = WorkflowStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new WorkflowPublished(Id, OrganizationId));
    }

    /// <summary>US-050: Deactivates the workflow; running executions complete but no new ones start.</summary>
    public void Archive()
    {
        if (Status == WorkflowStatus.Draft)
            throw new InvalidOperationException("Cannot archive a draft workflow.");

        if (Status == WorkflowStatus.Archived)
            throw new InvalidOperationException("Workflow is already archived.");

        Status = WorkflowStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new WorkflowArchived(Id, OrganizationId));
    }

    /// <summary>US-052: Soft-deletes a draft workflow.</summary>
    public void Delete()
    {
        if (Status != WorkflowStatus.Draft)
            throw new InvalidOperationException("Only draft workflows can be deleted.");

        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>US-050: Restores an archived workflow to Active.</summary>
    public void Unarchive()
    {
        if (Status != WorkflowStatus.Archived)
            throw new InvalidOperationException("Only archived workflows can be unarchived.");

        Status = WorkflowStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>US-051: Creates a full copy as a new Draft. Webhook URLs are NOT copied.</summary>
    public WorkflowDefinition Duplicate()
    {
        WorkflowDefinition copy = Create($"Copy of {Name}", Description, OrganizationId, CreatedBy);
        copy._triggers.AddRange(_triggers);

        // Map old step IDs to new step IDs
        Dictionary<Guid, Guid> idMap = new();

        // Remove the default Start/End from the copy (we'll re-add mapped ones)
        copy._steps.Clear();

        foreach (WorkflowStep step in _steps)
        {
            WorkflowStep newStep = WorkflowStep.Create(step.Name, step.Type, step.Config);
            idMap[step.Id] = newStep.Id;
            copy._steps.Add(newStep);
        }

        foreach (StepTransition transition in _transitions)
        {
            copy._transitions.Add(new StepTransition(
                idMap[transition.FromStepId],
                idMap[transition.ToStepId],
                transition.Label));
        }

        return copy;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static void ValidateName(string name)
    {
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length < 2 || trimmed.Length > 200)
            throw new ArgumentException("Workflow name must be 2–200 characters.");
    }

    private void EnsureStepExists(Guid stepId)
    {
        if (!_steps.Any(s => s.Id == stepId))
            throw new InvalidOperationException($"Step '{stepId}' not found in this workflow.");
    }

    /// <summary>DFS reachability: can we reach <paramref name="target"/> starting from <paramref name="start"/>?</summary>
    private bool CanReach(Guid start, Guid target)
    {
        if (start == target) return true;

        HashSet<Guid> visited = new();
        Queue<Guid> queue = new();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (StepTransition t in _transitions.Where(t => t.FromStepId == current))
            {
                if (t.ToStepId == target) return true;
                queue.Enqueue(t.ToStepId);
            }
        }

        return false;
    }
}
