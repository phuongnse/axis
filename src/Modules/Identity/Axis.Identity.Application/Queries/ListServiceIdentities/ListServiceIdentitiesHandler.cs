using Axis.Identity.Application.Commands.ManageServiceIdentity;
using Axis.Identity.Application.Repositories;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ListServiceIdentities;

public sealed class ListServiceIdentitiesHandler(
    IWorkspaceMembershipRepository memberships,
    IServiceIdentityRepository identities)
    : IQueryHandler<ListServiceIdentitiesQuery, Result<IReadOnlyList<ServiceIdentityDto>>>
{
    public async Task<Result<IReadOnlyList<ServiceIdentityDto>>> Handle(
        ListServiceIdentitiesQuery query,
        CancellationToken cancellationToken)
    {
        if (!await CreateServiceIdentityHandler.IsAdministrator(
                memberships,
                query.WorkspaceId,
                query.ActorUserId,
                cancellationToken))
            return Result.Failure<IReadOnlyList<ServiceIdentityDto>>(
                ErrorCodes.Forbidden,
                "Active Workspace Administrator membership is required.");

        IReadOnlyList<Domain.Aggregates.ServiceIdentity> values =
            await identities.ListAsync(query.WorkspaceId, cancellationToken);
        return Result.Success<IReadOnlyList<ServiceIdentityDto>>(
            values.Select(ServiceIdentityDtoMapping.ToDto).ToArray());
    }
}
