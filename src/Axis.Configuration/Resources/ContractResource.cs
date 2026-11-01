using System.Text.Json;

namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>contract</c> resource: what a module guarantees about one of its entities. Only a module holds
/// one. The schema checks its shape. Its entity and its lifecycle are checked by
/// <see cref="Compilation.ContractChecker"/>. The validations, effects and ceiling pass the schema and are
/// kept as written, until the checks that build them exist.
/// </summary>
public sealed record ContractResource : Resource
{
    /// <summary>The name of the covered entity, as written. It must be a plain name of the same module.</summary>
    public required string Entity { get; init; }

    /// <summary>The lifecycle of the entity, or null when the contract has none.</summary>
    public ContractLifecycleDefinition? Lifecycle { get; init; }

    /// <summary>The core validations as written, or null when the contract has none.</summary>
    public JsonElement? Validations { get; init; }

    /// <summary>The effects of transitions as written, or null when the contract has none.</summary>
    public JsonElement? Effects { get; init; }

    /// <summary>The grant ceiling as written, or null when the contract has none.</summary>
    public JsonElement? Ceiling { get; init; }
}

/// <summary>The lifecycle of a contract as written: a state machine on one enum field.</summary>
public sealed record ContractLifecycleDefinition
{
    /// <summary>The name of the enum field that holds the state.</summary>
    public required string Field { get; init; }

    /// <summary>The states, in declaration order.</summary>
    public required IReadOnlyList<LifecycleStateDefinition> States { get; init; }

    /// <summary>The transitions, in declaration order. The list may be empty.</summary>
    public IReadOnlyList<LifecycleTransitionDefinition> Transitions { get; init; } = [];
}

/// <summary>A state of a lifecycle as written.</summary>
public sealed record LifecycleStateDefinition
{
    public required string Name { get; init; }

    /// <summary>Whether a record may be created in this state.</summary>
    public bool? Initial { get; init; }

    /// <summary>Whether a record may be deleted in this state.</summary>
    public bool? Deletable { get; init; }
}

/// <summary>A transition of a lifecycle as written.</summary>
public sealed record LifecycleTransitionDefinition
{
    public required string Name { get; init; }

    /// <summary>The states the transition leaves, at least one.</summary>
    public required IReadOnlyList<string> From { get; init; }

    /// <summary>The state the transition enters.</summary>
    public required string To { get; init; }

    /// <summary>A boolean expression that must be true for the transition, or null for none.</summary>
    public string? Guard { get; init; }

    /// <summary>The text shown when the guard refuses the write, or null for a fixed text.</summary>
    public TextReference? Message { get; init; }
}
