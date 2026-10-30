namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.given_roles</c>: a role that an administrator gave a person, in one application or, with a null
/// <see cref="ApplicationId"/>, on the platform. A session reads these rows at every request. A row for an application role
/// that the application's active release does not declare stays but grants nothing.
/// </summary>
public sealed class GivenRoleRow
{
    public required Guid Id { get; init; }

    /// <summary>The id of the person in the tenant's realm.</summary>
    public required string UserId { get; init; }

    /// <summary>The application the role belongs to, or null for a platform role.</summary>
    public Guid? ApplicationId { get; init; }

    public required string Role { get; init; }
}
