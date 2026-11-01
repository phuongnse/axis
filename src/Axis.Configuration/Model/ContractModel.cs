using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled module contract on an entity of a pinned module. Only the lifecycle is compiled so far.
/// Every record write checks the lifecycle, see <c>RecordCommands</c> in <c>Axis.Data</c>.
/// </summary>
public sealed record ContractModel
{
    /// <summary>The contract's name, qualified with the module id as <c>&lt;id&gt;/&lt;name&gt;</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The contract's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The lifecycle of the entity, or null when the contract has none.</summary>
    public LifecycleModel? Lifecycle { get; init; }
}

/// <summary>A compiled lifecycle: a state machine on one enum field of the entity.</summary>
public sealed record LifecycleModel
{
    /// <summary>The declared name of the state field.</summary>
    public required string Field { get; init; }

    /// <summary>The states, in declaration order. Each is exactly one value of the field.</summary>
    public required IReadOnlyList<LifecycleStateModel> States { get; init; }

    /// <summary>The transitions, in declaration order.</summary>
    public required IReadOnlyList<LifecycleTransitionModel> Transitions { get; init; }
}

/// <summary>A state of a lifecycle. <see cref="Initial"/> allows a create in it, and <see cref="Deletable"/> a delete.</summary>
public sealed record LifecycleStateModel(string Name, bool Initial, bool Deletable);

/// <summary>A transition of a lifecycle from one or more states to another.</summary>
public sealed record LifecycleTransitionModel
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> From { get; init; }

    public required string To { get; init; }

    /// <summary>The boolean guard, or null when the transition is allowed whenever its states match.</summary>
    public ExpressionModel? Guard { get; init; }

    /// <summary>The text shown when the guard refuses the write, or null for a fixed text.</summary>
    public TextReference? Message { get; init; }
}
