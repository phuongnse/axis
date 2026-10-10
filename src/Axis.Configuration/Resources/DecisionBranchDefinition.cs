namespace Axis.Configuration.Resources;

/// <summary>A branch of a decision step as written: a boolean expression over the subject record, and the step taken when it is true.</summary>
public sealed record DecisionBranchDefinition
{
    public required string When { get; init; }

    public required string Next { get; init; }
}
