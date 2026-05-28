using Axis.DataModeling.Application.Services;
using Axis.FormBuilder.Contracts.Grpc;
using Axis.Shared.Domain.Primitives;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Axis.DataModeling.Infrastructure.Grpc;

/// <summary>blocks model deletion when FormBuilder reports active Relation Picker refs (ADR-014 gRPC).</summary>
internal sealed class FormModelDeletionGuard(
    FormModelReferenceService.FormModelReferenceServiceClient formModelReferences,
    IHttpContextAccessor httpContextAccessor)
    : IModelDeletionGuard
{
    public async Task<Result> ValidateCanDeleteAsync(
        Guid modelId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        Metadata headers = BuildAuthorizationMetadata();

        CountActiveModelReferencesResponse response;
        try
        {
            response = await formModelReferences.CountActiveModelReferencesAsync(
                new CountActiveModelReferencesRequest
                {
                    ModelId = modelId.ToString(),
                    OrganizationId = organizationId.ToString(),
                },
                headers: headers,
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated
                                      || ex.StatusCode == StatusCode.PermissionDenied)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Unable to verify form references for model deletion.");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Form reference check is temporarily unavailable. Try again shortly.");
        }

        if (response.ActiveReferenceCount > 0)
        {
            return Result.Failure(
                ErrorCodes.Conflict,
                $"This model is used by {response.ActiveReferenceCount} form(s). Remove those references before deleting.");
        }

        return Result.Success();
    }

    private Metadata BuildAuthorizationMetadata()
    {
        Metadata headers = new();
        string? authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
            headers.Add("authorization", authorization);

        return headers;
    }
}
