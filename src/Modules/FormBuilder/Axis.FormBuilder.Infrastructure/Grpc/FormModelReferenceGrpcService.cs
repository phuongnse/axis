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
        if (!Guid.TryParse(request.ModelId, out Guid modelId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));
        }

        Guid organizationId = ResolveCallerOrganizationId(context);

        int count = await references.CountActiveReferencesToModelAsync(
            modelId, organizationId, context.CancellationToken);

        return new CountActiveModelReferencesResponse { ActiveReferenceCount = count };
    }

    private static Guid ResolveCallerOrganizationId(ServerCallContext context)
    {
        Claim? claim = context.GetHttpContext().User.FindFirst("org_id");
        if (claim is null || !Guid.TryParse(claim.Value, out Guid organizationId))
        {
            throw new RpcException(
                new Status(StatusCode.Unauthenticated, "Caller JWT is missing a valid org_id claim."));
        }

        return organizationId;
    }
}
