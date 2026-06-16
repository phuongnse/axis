using System.Security.Claims;
using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Contracts.Grpc;
using Grpc.Core;

namespace Axis.FormBuilder.Infrastructure.Grpc;

internal sealed class FormModelReferenceGrpcService(IFormModelReferenceRepository references)
    : FormModelReferenceService.FormModelReferenceServiceBase
{
    public override async Task<CountActiveModelReferencesResponse> CountActiveModelReferences(
        CountActiveModelReferencesRequest request,
        ServerCallContext context)
    {
        Guid workspaceId = ResolveCallerworkspaceId(context);

        if (!Guid.TryParse(request.ModelId, out Guid modelId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));
        }

        int count = await references.CountActiveReferencesToModelAsync(
            modelId, workspaceId, context.CancellationToken);

        return new CountActiveModelReferencesResponse { ActiveReferenceCount = count };
    }

    private static Guid ResolveCallerworkspaceId(ServerCallContext context)
    {
        Claim? claim = context.GetHttpContext().User.FindFirst("workspace_id");
        if (claim is null || !Guid.TryParse(claim.Value, out Guid workspaceId))
        {
            throw new RpcException(
                new Status(StatusCode.Unauthenticated, "Caller JWT is missing a valid workspace_id claim."));
        }

        return workspaceId;
    }
}
