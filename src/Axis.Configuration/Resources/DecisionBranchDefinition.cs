namespace Axis.Configuration.Resources;

/// <summary>A branch of a decision step as written: its name, a boolean expression over the subject record, the step taken when it is true, and an optional label.</summary>
public sealed record DecisionBranchDefinition
{
    public required string Name { get; init; }

    public required string When { get; init; }

    public required string Next { get; init; }

    /// <summary>The label of the branch, shown where the instance's graph names it, or null.</summary>
    public TextReference? Label { get; init; }
}
