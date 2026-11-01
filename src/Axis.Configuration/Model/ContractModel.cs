using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled module contract on an entity of a pinned module. The lifecycle, the effects and the grant
/// ceiling are compiled so far. Nothing runs the effects yet. Every record write checks the lifecycle, see <c>RecordCommands</c> in <c>Axis.Data</c>.
/// The filters of the grant ceiling narrow a role's grants when a request runs, see <c>PolicyEvaluator.Access</c>.
/// </summary>
public sealed record ContractModel
{
    /// <summary>The contract's name, qualified with the module id as <c>&lt;id&gt;/&lt;name&gt;</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The contract's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The lifecycle of the entity, or null when the contract has none.</summary>
    public LifecycleModel? Lifecycle { get; init; }

    /// <summary>The effects of transitions, in declaration order. The list is empty when the contract has none.</summary>
    public IReadOnlyList<ContractEffectModel> Effects { get; init; } = [];

    /// <summary>The grant ceiling, or null when the contract has none.</summary>
    public CeilingModel? Ceiling { get; init; }
}

/// <summary>
/// A compiled effect of a transition: the record of <see cref="Entity"/> that a record's
/// <see cref="On"/> transition creates. <see cref="Values"/> hold the compiled expression of each field the
/// effect sets, in the entity's field order.
/// </summary>
public sealed record ContractEffectModel(string Name, string On, EntityReference Entity, IReadOnlyList<RecordAssignmentModel> Values);

/// <summary>
/// A compiled grant ceiling: the entries of each application role that some role slot of the module maps
/// to, and the entries of every other role. The compile has already refused a grant past it. At run
/// time the entries' filters narrow the grants of the role on the entity.
/// </summary>
public sealed record CeilingModel
{
    /// <summary>The entries of each application role mapped to one or more slots of the module, by role name.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<CeilingEntryModel>> Roles { get; init; }

    /// <summary>The entries of every role mapped to no slot of the module, or null when the contract has none.</summary>
    public IReadOnlyList<CeilingEntryModel>? Others { get; init; }
}

/// <summary>
/// An entry of a ceiling. <see cref="Fields"/> are declared names, and null means every field.
/// <see cref="Filter"/> is a policy filter, or null for none.
/// </summary>
public sealed record CeilingEntryModel(IReadOnlyList<PolicyAction> Actions, IReadOnlyList<string>? Fields, ExpressionModel? Filter);

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
