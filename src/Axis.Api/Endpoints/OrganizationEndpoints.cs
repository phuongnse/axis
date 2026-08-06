using System.ComponentModel.DataAnnotations;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.Identity.Application.Commands.CreateOrganizationWorkspace;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/organizations")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithTags("Identity");

        group.MapPost("", CreateOrganizationWorkspace)
            .WithName("CreateOrganizationWorkspace")
            .WithSummary("Create an organization and its initial workspace")
            .Produces<CreateOrganizationWorkspaceDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem()
            .WithMetadata(RequiredIdempotencyKeyMetadata.Instance);

        return app;
    }

    private static async Task<IResult> CreateOrganizationWorkspace(
        [FromBody] CreateOrganizationWorkspaceRequest request,
        [FromHeader(Name = "Idempotency-Key"), Required] string? idempotencyKey,
        HttpContext context,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<CreateOrganizationWorkspaceDto> result = await mediator.Send(
            new CreateOrganizationWorkspaceCommand(
                currentUser.UserId,
                request.Name,
                idempotencyKey ?? string.Empty,
                CorrelationId(context)),
            cancellationToken);

        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Json(result.Value, statusCode: StatusCodes.Status201Created);
    }

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value)
            && value is string correlationId
                ? correlationId
                : context.TraceIdentifier;
}
