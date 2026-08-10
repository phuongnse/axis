using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListServiceIdentities;

public sealed record ListServiceIdentitiesQuery(
    Guid ActorUserId,
    Guid WorkspaceId) : IQuery<Result<IReadOnlyList<ServiceIdentityDto>>>;
