namespace Axis.Configuration.Resources;

/// <summary>
/// A process step as written in the process file. The properties a step's <see cref="Type"/> needs
/// are required by the JSON Schema. Whether its transitions name steps of the process is checked
/// by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ProcessStepDefinition
{
    public const string Decision = "decision";
    public const string End = "end";

    public required string Name { get; init; }

    /// <summary>The step type: <c>decision</c> or <c>end</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The ordered branches of a decision, or null for any other step.</summary>
    public IReadOnlyList<DecisionBranchDefinition>? Branches { get; init; }

    /// <summary>The step a decision takes when no branch is true, or null for any other step.</summary>
    public string? Otherwise { get; init; }
}
