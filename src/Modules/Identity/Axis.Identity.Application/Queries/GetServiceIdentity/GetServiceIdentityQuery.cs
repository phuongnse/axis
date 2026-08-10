using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
namespace Axis.Identity.Application.Queries.GetServiceIdentity;

public sealed record GetServiceIdentityQuery(Guid ActorUserId, Guid WorkspaceId, Guid ServiceIdentityId) : IQuery<Result<ServiceIdentityDto>>;
