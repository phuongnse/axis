using System.Security.Claims;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Contracts.Grpc;
using Grpc.Core;

namespace Axis.WorkflowBuilder.Infrastructure.Grpc;

internal sealed class WorkflowFormReferenceGrpcService(IWorkflowReferenceRepository references)
    : WorkflowFormReferenceService.WorkflowFormReferenceServiceBase
{
    public override async Task<CountBlockingFormReferencesResponse> CountBlockingFormReferences(
        CountBlockingFormReferencesRequest request,
        ServerCallContext context)
    {
        Guid tenantId = ResolveCallertenantId(context);

        if (!Guid.TryParse(request.FormId, out Guid formId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid form_id."));

        int count = await references.CountBlockingFormReferencesAsync(
            formId,
            tenantId,
            context.CancellationToken);

        return new CountBlockingFormReferencesResponse { BlockingReferenceCount = count };
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
