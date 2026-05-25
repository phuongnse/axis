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
        if (!Guid.TryParse(request.FormId, out Guid formId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid form_id."));

        if (!Guid.TryParse(request.OrganizationId, out Guid organizationId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid organization_id."));

        int count = await references.CountBlockingFormReferencesAsync(
            formId,
            organizationId,
            context.CancellationToken);

        return new CountBlockingFormReferencesResponse { BlockingReferenceCount = count };
    }
}
