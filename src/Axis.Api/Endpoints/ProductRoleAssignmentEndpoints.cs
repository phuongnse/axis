using System.ComponentModel.DataAnnotations;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Identity.Application.Queries.ListAssignableSubjects;
using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class ProductRoleAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapProductRoleAssignmentEndpoints(
        this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/product-role-assignments")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithTags("Authorization");

        group.MapPost("/assign", Assign)
            .WithName("AssignProductRole")
            .WithSummary("Assign an exact installed product role to a current-Workspace subject")
            .Produces<ProductRoleAssignmentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(RequiredIdempotencyKeyMetadata.Instance);

        group.MapPost("/revoke", Revoke)
            .WithName("RevokeProductRole")
            .WithSummary("Revoke an exact product-role assignment")
            .Produces<ProductRoleAssignmentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithMetadata(RequiredIdempotencyKeyMetadata.Instance);

        group.MapGet("", List)
            .WithName("ListProductRoleAssignments")
            .WithSummary("List active assignable subjects, installed product roles, and assignment state")
            .Produces<ProductRoleManagementResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> Assign(
        [FromBody] AssignProductRoleBody request,
        [FromHeader(Name = RequiredIdempotencyKeyMetadata.HeaderName), Required]
        string? idempotencyKey,
        HttpContext context,
        CurrentUser currentUser,
        ProductRoleAssignmentService service,
        CancellationToken cancellationToken)
    {
        TrySubject(request.Target, out SubjectReference target);

        ProductRoleAssignmentResult result = await service.AssignAsync(
            new AssignProductRoleRequest(
                currentUser.WorkspaceId,
                new SubjectReference(SubjectKind.Human, currentUser.UserId),
                target,
                request.PolicyVersionId,
                request.RoleKey ?? string.Empty,
                idempotencyKey ?? string.Empty,
                CorrelationId(context),
                currentUser.DisplayName,
                request.ExpectedRevision),
            cancellationToken);

        return ToResult(result);
    }

    private static async Task<IResult> Revoke(
        [FromBody] RevokeProductRoleBody request,
        [FromHeader(Name = RequiredIdempotencyKeyMetadata.HeaderName), Required]
        string? idempotencyKey,
        HttpContext context,
        CurrentUser currentUser,
        ProductRoleAssignmentService service,
        CancellationToken cancellationToken)
    {
        TrySubject(request.Target, out SubjectReference target);

        ProductRoleAssignmentResult result = await service.RevokeAsync(
            new RevokeProductRoleRequest(
                currentUser.WorkspaceId,
                new SubjectReference(SubjectKind.Human, currentUser.UserId),
                target,
                request.PolicyVersionId,
                request.RoleKey ?? string.Empty,
                idempotencyKey ?? string.Empty,
                CorrelationId(context),
                currentUser.DisplayName,
                request.ExpectedRevision),
            cancellationToken);

        return ToResult(result);
    }

    private static async Task<IResult> List(
        [FromQuery] string? language,
        CurrentUser currentUser,
        ISender sender,
        ProductRoleManagementQueryService service,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AssignableSubjectDto>> subjects = await sender.Send(
            new ListAssignableSubjectsQuery(currentUser.UserId, currentUser.WorkspaceId),
            cancellationToken);
        if (subjects.IsFailure)
            return Problem(StatusCodes.Status404NotFound, "authorization.assignment.unavailable");

        ProductRoleManagementResult state = await service.GetAsync(
            currentUser.WorkspaceId,
            SubjectReference.Human(currentUser.UserId),
            language ?? "en",
            cancellationToken);
        return state.IsSuccess
            ? Results.Ok(new ProductRoleManagementResponse(subjects.Value, state.Roles, state.Assignments))
            : Problem(
                state.Error == "invalid"
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status404NotFound,
                "authorization.assignment.unavailable");
    }

    private static IResult ToResult(ProductRoleAssignmentResult result)
    {
        if (result.IsSuccess && result.Assignment is not null)
            return Results.Ok(ProductRoleAssignmentResponse.From(result.Assignment));

        string error = result.Error ?? "unavailable";
        int statusCode = error switch
        {
            "invalid" => StatusCodes.Status400BadRequest,
            "authority_denied" or "target_inactive" or "role_unavailable" =>
                StatusCodes.Status404NotFound,
            "idempotency_conflict" or "revision_conflict" or "assignment_inactive" or
                "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable,
        };
        string code = error is "authority_denied" or "target_inactive" or "role_unavailable"
            ? "authorization.assignment.unavailable"
            : $"authorization.assignment.{error}";
        ProblemDetails problem = ProblemDetailsDefaults.CreateProblemDetails(
            statusCode,
            "The product-role assignment request could not be completed.",
            code);
        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: ProblemDetailsDefaults.JsonContentType);
    }

    private static IResult Problem(int statusCode, string code)
    {
        ProblemDetails problem = ProblemDetailsDefaults.CreateProblemDetails(
            statusCode,
            "Product-role assignment management is unavailable.",
            code);
        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: ProblemDetailsDefaults.JsonContentType);
    }

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value)
            && value is string correlationId
                ? correlationId
                : context.TraceIdentifier;

    private static bool TrySubject(SubjectReferenceDto? value, out SubjectReference subject)
    {
        bool valid = value is not null &&
            value.SubjectId != Guid.Empty &&
            Enum.IsDefined(value.Kind);
        subject = valid ? new SubjectReference(value!.Kind, value.SubjectId) : default;
        return valid;
    }

    public sealed record AssignProductRoleBody(
        SubjectReferenceDto Target,
        Guid PolicyVersionId,
        string RoleKey,
        int? ExpectedRevision = null);

    public sealed record RevokeProductRoleBody(
        SubjectReferenceDto Target,
        Guid PolicyVersionId,
        string RoleKey,
        int ExpectedRevision);

    public sealed record ProductRoleAssignmentResponse(
        Guid WorkspaceId,
        SubjectReferenceDto Subject,
        Guid PolicyVersionId,
        string RoleKey,
        bool IsActive,
        int Revision)
    {
        internal static ProductRoleAssignmentResponse From(ProductRoleAssignment value) =>
            new(
                value.WorkspaceId,
                SubjectReferenceDto.From(value.Subject),
                value.PolicyVersionId,
                value.RoleKey,
                value.IsActive,
                value.Revision);
    }

    public sealed record ProductRoleManagementResponse(
        IReadOnlyList<AssignableSubjectDto> Subjects,
        IReadOnlyList<ProductRoleOptionDto> Roles,
        IReadOnlyList<ProductRoleAssignmentDto> Assignments);
}
