namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>process</c> resource: named steps and the transitions between them, run for one record of
/// its subject entity, or, without a subject, for the values of its parameters. Its entity,
/// parameters, step graph and expressions are checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record ProcessResource : Resource
{
    /// <summary>The subject entity, or null for a process without a subject, which declares <see cref="Parameters"/> instead.</summary>
    public string? Entity { get; init; }

    /// <summary>The typed parameters of a process without a subject, or null when the process has a subject.</summary>
    public IReadOnlyList<ProcessParameterDefinition>? Parameters { get; init; }

    /// <summary>The condition a subject record must meet for a start, or null when every start is allowed.</summary>
    public ProcessStartConditionDefinition? StartCondition { get; init; }

    /// <summary>The name of the first step.</summary>
    public required string Start { get; init; }

    /// <summary>The steps, in file order.</summary>
    public required IReadOnlyList<ProcessStepDefinition> Steps { get; init; }
}
