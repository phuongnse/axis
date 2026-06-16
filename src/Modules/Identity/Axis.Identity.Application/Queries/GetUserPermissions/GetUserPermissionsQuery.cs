using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.GetUserPermissions;

/// <summary>Resolves effective RBAC permissions for a user within a team account (gRPC + internal sync).</summary>
public sealed record GetUserPermissionsQuery(Guid UserId, Guid TeamAccountId)
    : IQuery<Result<GetUserPermissionsResult>>;

public sealed record GetUserPermissionsResult(IReadOnlyList<string> Permissions);
