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
        Guid tenantId = ResolveCallertenantId(context);

        if (!Guid.TryParse(request.ModelId, out Guid modelId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));
        }

        int count = await references.CountActiveReferencesToModelAsync(
            modelId, tenantId, context.CancellationToken);

        return new CountActiveModelReferencesResponse { ActiveReferenceCount = count };
    }

    private static Guid ResolveCallertenantId(ServerCallContext context)
    {
        Claim? claim = context.GetHttpContext().User.FindFirst("tenant_id");
        if (claim is null || !Guid.TryParse(claim.Value, out Guid tenantId))
        {
            throw new RpcException(
                new Status(StatusCode.Unauthenticated, "Caller JWT is missing a valid tenant_id claim."));
        }

        return tenantId;
    }
}
