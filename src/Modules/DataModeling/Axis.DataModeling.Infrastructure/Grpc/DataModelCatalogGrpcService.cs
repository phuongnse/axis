using System.Security.Claims;
using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Contracts.Grpc;
using Axis.DataModeling.Domain.Aggregates;
using Grpc.Core;

namespace Axis.DataModeling.Infrastructure.Grpc;

internal sealed class DataModelCatalogGrpcService(IDataModelRepository dataModelRepository)
    : DataModelCatalogService.DataModelCatalogServiceBase
{
    public override async Task<GetModelSummaryResponse> GetModelSummary(
        GetModelSummaryRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ModelId, out Guid modelId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));
        }

        Guid organizationId = ResolveCallerOrganizationId(context);

        DataModel? model = await dataModelRepository.GetByIdAsync(
            modelId,
            organizationId,
            context.CancellationToken);

        return new GetModelSummaryResponse
        {
            Exists = model is not null,
            ModelName = model?.Name ?? string.Empty,
        };
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
