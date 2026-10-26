namespace Axis.Configuration.Model;

/// <summary>
/// A compiled policy: the declared roles it applies to and its grants, each with its target
/// resolved. The record API and the data source endpoint enforce its entity and data source
/// grants. The record API applies a grant's filter and fields, and the data source endpoint counts
/// only a grant with neither. The process start enforces its process grants, and the endpoint call
/// its endpoint grants.
/// </summary>
public sealed record PolicyModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The policy's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The names of the roles the grants apply to, as written.</summary>
    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>The grants, in declaration order.</summary>
    public required IReadOnlyList<GrantModel> Grants { get; init; }
}

/// <summary>
/// A compiled grant. Exactly one of <see cref="Entity"/>, a root entity, <see cref="DataSource"/>,
/// <see cref="Process"/> and <see cref="Endpoint"/> is set, and every action is one its target
/// takes. Only an entity grant has a <see cref="Filter"/>, a boolean expression over the entity's
/// fields and paths through reference fields, inside the SQL subset, that can call
/// <c>currentUser()</c>, and <see cref="Fields"/>, the entity's field names as declared, or null
/// for every field.
/// </summary>
public sealed record GrantModel(
    EntityReference? Entity,
    DataSourceReference? DataSource,
    ProcessReference? Process,
    EndpointReference? Endpoint,
    IReadOnlyList<PolicyAction> Actions,
    ExpressionModel? Filter,
    IReadOnlyList<string>? Fields);

/// <summary>
/// What a grant allows. An entity grant takes <see cref="Read"/>, <see cref="Create"/>,
/// <see cref="Update"/>, <see cref="Delete"/>, <see cref="Export"/> and <see cref="Import"/>, a
/// data source grant <see cref="Read"/> and <see cref="Export"/>, a
/// process grant <see cref="Start"/>, <see cref="Inspect"/>, <see cref="Cancel"/>, <see cref="Retry"/>, <see cref="Resolve"/> and <see cref="Reassign"/>, and an endpoint grant
/// <see cref="Invoke"/>.
/// </summary>
public enum PolicyAction
{
    Read,
    Create,
    Update,
    Delete,
    Start,
    Invoke,
    Inspect,
    Cancel,
    Retry,
    Resolve,
    Reassign,
    Export,
    Import,
}
