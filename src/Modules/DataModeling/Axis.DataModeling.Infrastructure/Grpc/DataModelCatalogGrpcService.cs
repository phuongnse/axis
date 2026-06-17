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
        Guid workspaceId = ResolveCallerworkspaceId(context);

        if (!Guid.TryParse(request.ModelId, out Guid modelId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));
        }

        DataModel? model = await dataModelRepository.GetByIdAsync(
            modelId,
            workspaceId,
            context.CancellationToken);

        return new GetModelSummaryResponse
        {
            Exists = model is not null,
            ModelName = model?.Name ?? string.Empty,
        };
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
