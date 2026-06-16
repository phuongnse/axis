using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.FormBuilder.Application.Commands.SubmitFormByToken;
using Axis.FormBuilder.Application.Queries.GetFormTaskByToken;
using Axis.FormBuilder.Application.Queries.GetMyFormTasks;
using Axis.FormBuilder.Domain.Enums;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class FormTaskEndpoints
{
    public static IEndpointRouteBuilder MapFormTaskEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder publicGroup = app.MapGroup("/api/form-tasks");

        publicGroup.MapGet("/{accessToken:guid}", GetFormTaskByToken)
            .AllowAnonymous()
            .WithName("GetFormTaskByToken")
            .WithSummary("Load a form task for standalone submission (token-based access)")
            .WithTags("FormBuilder")
            .Produces<FormTaskByTokenDto>()
            .ProducesProblem(404);

        publicGroup.MapPost("/{accessToken:guid}/submit", SubmitFormByToken)
            .AllowAnonymous()
            .WithName("SubmitFormByToken")
            .WithSummary("Submit responses for a form task (no login required)")
            .WithTags("FormBuilder")
            .Produces(204)
            .ProducesProblem(404)
            .ProducesProblem(422);

        RouteGroupBuilder mine = app.MapGroup("/api/form-tasks/mine")
            .RequireAuthorization();

        mine.MapGet("/pending", GetMyPendingTasks)
            .RequireAuthorization(Permissions.Form.Submit)
            .WithName("GetMyPendingFormTasks")
            .WithSummary("List pending form tasks assigned to the current user")
            .WithTags("FormBuilder")
            .Produces<IReadOnlyList<FormTaskSummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        mine.MapGet("/completed", GetMyCompletedTasks)
            .RequireAuthorization(Permissions.Form.Submit)
            .WithName("GetMyCompletedFormTasks")
            .WithSummary("List completed form tasks for the current user")
            .WithTags("FormBuilder")
            .Produces<IReadOnlyList<FormTaskSummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        return app;
    }

    private static async Task<IResult> GetFormTaskByToken(
        Guid accessToken,
        ISender mediator,
        CancellationToken ct)
    {
        FormTaskByTokenDto? result = await mediator.Send(new GetFormTaskByTokenQuery(accessToken), ct);
        if (result is null) return Results.NotFound();
        return Results.Ok(result);
    }

    private static async Task<IResult> SubmitFormByToken(
        Guid accessToken,
        [FromBody] SubmitFormByTokenRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new SubmitFormByTokenCommand(accessToken, request.Data), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> GetMyPendingTasks(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        IReadOnlyList<FormTaskSummaryDto> result = await mediator.Send(
            new GetMyFormTasksQuery(currentUser.UserId, currentUser.OrgId, FormSubmissionStatus.Pending), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyCompletedTasks(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        IReadOnlyList<FormTaskSummaryDto> result = await mediator.Send(
            new GetMyFormTasksQuery(currentUser.UserId, currentUser.OrgId, FormSubmissionStatus.Submitted), ct);
        return Results.Ok(result);
    }
}
