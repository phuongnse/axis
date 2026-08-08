using Axis.Identity.Application.Commands.ManageServiceIdentity;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
namespace Axis.Identity.Application.Queries.GetServiceIdentity;

public sealed class GetServiceIdentityHandler(IWorkspaceMembershipRepository memberships, IServiceIdentityRepository identities) : IQueryHandler<GetServiceIdentityQuery, Result<ServiceIdentityDto>>
{ public async Task<Result<ServiceIdentityDto>> Handle(GetServiceIdentityQuery q, CancellationToken ct) { ServiceIdentity? identity = await identities.GetAsync(q.WorkspaceId, q.ServiceIdentityId, ct); if (identity is null) return Result.Failure<ServiceIdentityDto>(ErrorCodes.NotFound, "Service identity was not found."); return await CreateServiceIdentityHandler.IsAdministrator(memberships, q.WorkspaceId, q.ActorUserId, ct) ? Result.Success(identity.ToDto()) : CreateServiceIdentityHandler.Forbidden(); } }
