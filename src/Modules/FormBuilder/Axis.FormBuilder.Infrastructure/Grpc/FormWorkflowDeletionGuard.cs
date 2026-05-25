using Axis.FormBuilder.Application.Services;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Contracts.Grpc;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Axis.FormBuilder.Infrastructure.Grpc;

/// <summary>US-078: blocks form deletion when WorkflowBuilder reports draft/active step refs (ADR-014 gRPC).</summary>
internal sealed class FormWorkflowDeletionGuard(
    WorkflowFormReferenceService.WorkflowFormReferenceServiceClient workflowReferences,
    IHttpContextAccessor httpContextAccessor)
    : IFormDeletionGuard
{
    private static readonly TimeSpan RpcDeadline = TimeSpan.FromSeconds(30);

    public async Task<Result> ValidateCanDeleteAsync(
        Guid formId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        Metadata headers = BuildAuthorizationMetadata();

        CountBlockingFormReferencesResponse response;
        try
        {
            response = await workflowReferences.CountBlockingFormReferencesAsync(
                new CountBlockingFormReferencesRequest
                {
                    FormId = formId.ToString(),
                    OrganizationId = organizationId.ToString(),
                },
                headers: headers,
                deadline: DateTime.UtcNow.Add(RpcDeadline),
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated
                                      || ex.StatusCode == StatusCode.PermissionDenied)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Unable to verify workflow references for form deletion.");
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Cancelled)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Workflow reference check timed out. Try again shortly.");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "Workflow reference check is temporarily unavailable. Try again shortly.");
        }

        if (response.BlockingReferenceCount > 0)
        {
            return Result.Failure(
                ErrorCodes.BusinessRule,
                "This form is referenced by one or more active workflow steps. Remove those references before deleting.");
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
