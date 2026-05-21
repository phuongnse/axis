using Axis.WorkflowEngine.Domain.Enums;

namespace Axis.WorkflowEngine.Domain.ReadModels;

/// <summary>
/// Immutable snapshot of a step definition captured at workflow publish time.
/// Stored in WorkflowSnapshot as JSONB — WorkflowEngine never queries WorkflowBuilder's DB directly.
/// </summary>
public sealed class StepDefinitionSnapshot
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public StepType StepType { get; init; }
    public int DisplayOrder { get; init; }
    public IReadOnlyDictionary<string, object?>? Config { get; init; }
}
