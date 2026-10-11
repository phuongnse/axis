namespace Axis.Configuration.Resources;

/// <summary>
/// A grant of a policy as written: one target, which is an <see cref="Entity"/>, a
/// <see cref="DataSource"/> or a <see cref="Process"/>, the actions it allows on that target, and,
/// for an entity, an optional row <see cref="Filter"/> and an optional list of
/// <see cref="Fields"/>. Whether exactly one target is set, and whether the actions, filter and
/// fields fit it, is checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record GrantDefinition
{
    public string? Entity { get; init; }

    public string? DataSource { get; init; }

    public string? Process { get; init; }

    /// <summary>The actions, each one of <c>read</c>, <c>create</c>, <c>update</c>, <c>delete</c> and <c>start</c>.</summary>
    public required IReadOnlyList<string> Actions { get; init; }

    /// <summary>A boolean expression over the entity's fields that limits the rows the grant covers.</summary>
    public string? Filter { get; init; }

    /// <summary>The entity fields the grant covers. Null covers every field.</summary>
    public IReadOnlyList<string>? Fields { get; init; }
}
