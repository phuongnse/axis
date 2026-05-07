using Axis.WorkflowBuilder.Domain.Enums;
using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowBuilder.Domain.Entities;

/// <summary>A single step node in a workflow definition.</summary>
public sealed class WorkflowStep : Entity<Guid>
{
    public string Name { get; private set; }
    public StepType Type { get; private set; }
    public IReadOnlyDictionary<string, object?>? Config { get; private set; }

    private WorkflowStep(Guid id, string name, StepType type, IReadOnlyDictionary<string, object?>? config)
        : base(id)
    {
        Name = name;
        Type = type;
        Config = config;
    }

    internal static WorkflowStep Create(string name, StepType type, IReadOnlyDictionary<string, object?>? config)
        => new(Guid.NewGuid(), name, type, config);

    public void UpdateConfig(string name, IReadOnlyDictionary<string, object?>? config)
    {
        Name = name;
        Config = config;
    }
}
