using System.Security.Claims;
using Axis.Identity.Application.Queries.GetUserPermissions;
using Axis.Identity.Contracts.Grpc;
using Axis.Shared.Domain.Primitives;
using Grpc.Core;
using MediatR;

namespace Axis.Identity.Infrastructure.Grpc;

internal sealed class IdentityGrpcService(IMediator mediator) : IdentityService.IdentityServiceBase
{
    public override async Task<GetUserPermissionsResponse> GetUserPermissions(
        GetUserPermissionsRequest request,
        ServerCallContext context)
    {
        Guid teamAccountId = ResolveCallerTeamAccountId(context);

        if (!Guid.TryParse(request.UserId, out Guid userId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "user_id must be a valid GUID."));
        }

        Result<GetUserPermissionsResult> result = await mediator.Send(
            new GetUserPermissionsQuery(userId, teamAccountId),
            context.CancellationToken);

        if (result.IsFailure)
        {
            if (result.ErrorCode == ErrorCodes.NotFound)
            {
                throw new RpcException(
                    new Status(StatusCode.NotFound, result.Error));
            }

            throw new RpcException(
                new Status(StatusCode.Internal, result.Error));
        }

        GetUserPermissionsResponse response = new();
        response.Permissions.AddRange(result.Value.Permissions);
        return response;
    }

    private static Guid ResolveCallerTeamAccountId(ServerCallContext context)
    {
        Claim? claim = context.GetHttpContext().User.FindFirst("team_account_id");
        if (claim is null || !Guid.TryParse(claim.Value, out Guid teamAccountId))
        {
            throw new RpcException(
                new Status(StatusCode.Unauthenticated, "Caller JWT is missing a valid team_account_id claim."));
        }

        return teamAccountId;
    }
}
