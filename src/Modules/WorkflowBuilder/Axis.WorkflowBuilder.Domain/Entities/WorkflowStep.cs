using Axis.WorkflowBuilder.Domain.Enums;
using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Entities;

/// <summary>A single step node in a workflow definition.</summary>
public sealed class WorkflowStep : Entity<Guid>
{
    public string Name { get; private set; }
    public StepType Type { get; private set; }
    public IReadOnlyDictionary<string, object?>? Config { get; private set; }

    private WorkflowStep() : base(default) { Name = null!; } // EF Core materialisation

    private WorkflowStep(Guid id, string name, StepType type, IReadOnlyDictionary<string, object?>? config)
        : base(id)
    {
        Name = name;
        Type = type;
        Config = config;
    }

    internal static WorkflowStep Create(string name, StepType type, IReadOnlyDictionary<string, object?>? config)
        => new(Guid.NewGuid(), name, type, config);

    internal static WorkflowStep Reconstitute(Guid id, string name, StepType type, IReadOnlyDictionary<string, object?>? config)
        => new(id, name, type, config);

    public void UpdateConfig(string name, IReadOnlyDictionary<string, object?>? config)
    {
        Name = name;
        Config = config;
    }

    /// <summary>
    /// Extracts the FormId from a Form step's config dictionary.
    /// Returns null if this is not a Form step or the formId key is absent/invalid.
    /// </summary>
    public Guid? TryGetFormId()
    {
        if (Type != StepType.Form || Config is null) return null;
        if (!Config.TryGetValue("formId", out object? raw) || raw is null) return null;
        // raw is JsonElement when read back from JSONB; JsonElement.ToString() returns the unquoted string value.
        return Guid.TryParse(raw.ToString(), out Guid id) ? id : null;
    }
}
