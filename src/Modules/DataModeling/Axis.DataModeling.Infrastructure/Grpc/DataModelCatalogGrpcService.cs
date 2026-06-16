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
        Guid teamAccountId = ResolveCallerTeamAccountId(context);

        if (!Guid.TryParse(request.ModelId, out Guid modelId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "model_id must be a valid GUID."));
        }

        DataModel? model = await dataModelRepository.GetByIdAsync(
            modelId,
            teamAccountId,
            context.CancellationToken);

        return new GetModelSummaryResponse
        {
            Exists = model is not null,
            ModelName = model?.Name ?? string.Empty,
        };
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
