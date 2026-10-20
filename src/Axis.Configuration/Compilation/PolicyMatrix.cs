namespace Axis.Configuration.Compilation;

/// <summary>
/// What each role may do on each root entity, data source, process and endpoint, as the authoring API's policy
/// matrix route answers it. <paramref name="Targets"/> are the columns in order: root entities, data sources,
/// processes, then endpoints. <paramref name="Roles"/> are the rows, and each row has one cell per column.
/// </summary>
public sealed record PolicyMatrix(IReadOnlyList<PolicyMatrixTarget> Targets, IReadOnlyList<PolicyMatrixRole> Roles)
{
    public static PolicyMatrix Empty { get; } = new([], []);
}

/// <summary>
/// A column. <paramref name="Kind"/> is <c>entity</c>, <c>dataSource</c>, <c>process</c> or <c>endpoint</c>.
/// <paramref name="Module"/> is the module id, or null for the application's own. <paramref name="Actions"/> are
/// the actions the column accepts. <paramref name="Entity"/> is the root entity of a data source, and null for
/// the other kinds.
/// </summary>
public sealed record PolicyMatrixTarget(
    string Kind,
    Guid Id,
    string Name,
    string? Module,
    IReadOnlyList<string> Actions,
    string? Entity);

/// <summary>
/// A row. <paramref name="AccessPolicy"/> is the role's <c>&lt;Role&gt;Access</c> policy. The row is
/// <paramref name="Editable"/> when matrix edits may change that policy, and <paramref name="ReadOnlyReason"/>
/// is a code that says why not otherwise.
/// </summary>
public sealed record PolicyMatrixRole(
    Guid Id,
    string Name,
    PolicyMatrixAccessPolicy AccessPolicy,
    bool Editable,
    string? ReadOnlyReason,
    IReadOnlyList<PolicyMatrixCell> Cells);

/// <summary>The name of a role's <c>&lt;Role&gt;Access</c> policy and its id, which is null when no such policy exists.</summary>
public sealed record PolicyMatrixAccessPolicy(string Name, Guid? Id);

/// <summary>
/// The actions a role has on one target, from all its policies combined with OR. <paramref name="Target"/> is the
/// column's name. <paramref name="MissingEntityRead"/> names the root entity of a data source when the role has a
/// <c>read</c> grant on the data source but cannot read that entity, and is null otherwise.
/// </summary>
public sealed record PolicyMatrixCell(
    string Kind,
    string Target,
    IReadOnlyList<string> Actions,
    string? MissingEntityRead,
    IReadOnlyList<PolicyMatrixGrant> Grants);

/// <summary>
/// One grant behind a cell, as written. <paramref name="Shared"/> is false only for a grant of the role's own
/// <c>&lt;Role&gt;Access</c> policy, which names no other role.
/// </summary>
public sealed record PolicyMatrixGrant(
    Guid PolicyId,
    string Policy,
    int Index,
    IReadOnlyList<string> Actions,
    string? Filter,
    IReadOnlyList<string>? Fields,
    bool Shared);
