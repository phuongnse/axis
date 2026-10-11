namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>policy</c> resource: the roles it applies to and the grants it gives them. The schema
/// checks the shape of each grant's properties. Its roles, targets, actions, fields and filters
/// are checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record PolicyResource : Resource
{
    /// <summary>The names of the roles the grants apply to.</summary>
    public required IReadOnlyList<string> Roles { get; init; }

    public required IReadOnlyList<GrantDefinition> Grants { get; init; }
}
