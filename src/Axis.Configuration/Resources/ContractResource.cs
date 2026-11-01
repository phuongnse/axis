namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>contract</c> resource: what a module guarantees about one of its entities. Only a module holds
/// one. The schema checks its shape. Its entity, its lifecycle, its core validations, its effects and its
/// ceiling are checked by <see cref="Compilation.ContractChecker"/>.
/// </summary>
public sealed record ContractResource : Resource
{
    /// <summary>The name of the covered entity, as written. It must be a plain name of the same module.</summary>
    public required string Entity { get; init; }

    /// <summary>The lifecycle of the entity, or null when the contract has none.</summary>
    public ContractLifecycleDefinition? Lifecycle { get; init; }

    /// <summary>The core validations as written, or null when the contract has none.</summary>
    public IReadOnlyList<ContractValidationDefinition>? Validations { get; init; }

    /// <summary>The effects of transitions in declaration order, or null when the contract has none.</summary>
    public IReadOnlyList<ContractEffectDefinition>? Effects { get; init; }

    /// <summary>The grant ceiling, or null when the contract has none.</summary>
    public ContractCeilingDefinition? Ceiling { get; init; }
}

/// <summary>An effect of a contract as written: when a transition happens, it creates a record of another entity.</summary>
public sealed record ContractEffectDefinition
{
    public required string Name { get; init; }

    /// <summary>The name of the transition of the same contract that runs the effect.</summary>
    public required string On { get; init; }

    /// <summary>The record the effect creates.</summary>
    public required ContractEffectCreateDefinition Create { get; init; }
}

/// <summary>The record an effect creates as written.</summary>
public sealed record ContractEffectCreateDefinition
{
    /// <summary>The name of the entity, as written. It must be a plain name of a root entity of the same module.</summary>
    public required string Entity { get; init; }

    /// <summary>The expression that gives the value of each field, by field name.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
}

/// <summary>A core validation of a contract as written: a boolean rule on the entity that runs on every write.</summary>
public sealed record ContractValidationDefinition
{
    /// <summary>The validation's name, unique in the contract ignoring letter case.</summary>
    public required string Name { get; init; }

    /// <summary>A boolean expression that must be true for the write.</summary>
    public required string Expression { get; init; }

    /// <summary>The text shown when the expression is not true. Its key starts with the module id.</summary>
    public required TextReference Message { get; init; }

    /// <summary>The name of the field the failure is reported at.</summary>
    public required string Field { get; init; }
}

/// <summary>The grant ceiling of a contract as written: the widest grants each role slot may get on the entity.</summary>
public sealed record ContractCeilingDefinition
{
    /// <summary>The entries of each role slot of the module, by slot name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CeilingEntryDefinition>> Slots { get; init; } =
        new Dictionary<string, IReadOnlyList<CeilingEntryDefinition>>();

    /// <summary>The entries of every role mapped to no slot of the module, or null when there are none.</summary>
    public IReadOnlyList<CeilingEntryDefinition>? Others { get; init; }
}

/// <summary>An entry of a ceiling as written: the shape of a grant on the entity without a target.</summary>
public sealed record CeilingEntryDefinition
{
    /// <summary>The entity actions grants may allow.</summary>
    public required IReadOnlyList<string> Actions { get; init; }

    /// <summary>The fields grants may cover, or null when every field may be covered.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>A policy filter that limits the records, or null for none.</summary>
    public string? Filter { get; init; }
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
