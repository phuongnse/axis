using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Commands.CancelExecution;
using Axis.WorkflowEngine.Application.Commands.RetryExecution;
using Axis.WorkflowEngine.Application.Commands.RetryExecutionWithContext;
using Axis.WorkflowEngine.Application.Commands.StartExecution;
using Axis.WorkflowEngine.Application.DTOs;
using Axis.WorkflowEngine.Application.Queries.GetAllExecutions;
using Axis.WorkflowEngine.Application.Queries.GetExecution;
using Axis.WorkflowEngine.Application.Queries.GetExecutionsByWorkflow;
using Axis.WorkflowEngine.Application.Queries.GetRetryHistory;
using Axis.WorkflowEngine.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class ExecutionEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder executions = app.MapGroup("/api/executions")
            .RequireAuthorization();

        executions.MapGet("/", GetAllExecutions)
            .RequireAuthorization(Permissions.Execution.Read)
            .WithName("GetAllExecutions")
            .WithSummary("List workflow executions for the tenant (paginated)")
            .WithTags("WorkflowEngine")
            .Produces<PagedResult<ExecutionSummaryResponse>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        executions.MapGet("/{executionId:guid}", GetExecution)
            .RequireAuthorization(Permissions.Execution.Read)
            .WithName("GetExecution")
            .WithSummary("Get execution detail with step timeline")
            .WithTags("WorkflowEngine")
            .Produces<ExecutionResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        executions.MapPost("/{executionId:guid}/cancel", CancelExecution)
            .RequireAuthorization(Permissions.Execution.Cancel)
            .WithName("CancelExecution")
            .WithSummary("Cancel a running or pending execution")
            .WithTags("WorkflowEngine")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        executions.MapPost("/{executionId:guid}/retry", RetryExecution)
            .RequireAuthorization(Permissions.Execution.Retry)
            .WithName("RetryExecution")
            .WithSummary("Retry a failed execution from the failed step")
            .WithTags("WorkflowEngine")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        executions.MapPost("/{executionId:guid}/retry-with-context", RetryExecutionWithContext)
            .RequireAuthorization(Permissions.Execution.Retry)
            .WithName("RetryExecutionWithContext")
            .WithSummary("Retry a failed execution using a modified context")
            .WithTags("WorkflowEngine")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        executions.MapGet("/{executionId:guid}/retry-history", GetRetryHistory)
            .RequireAuthorization(Permissions.Execution.Read)
            .WithName("GetRetryHistory")
            .WithSummary("List retry executions linked to an original execution")
            .WithTags("WorkflowEngine")
            .Produces<IReadOnlyList<ExecutionSummaryResponse>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        RouteGroupBuilder workflowExecutions = app.MapGroup("/api/workflows/{workflowId:guid}/executions")
            .RequireAuthorization();

        workflowExecutions.MapGet("/", GetExecutionsByWorkflow)
            .RequireAuthorization(Permissions.Execution.Read)
            .WithName("GetExecutionsByWorkflow")
            .WithSummary("List executions for a workflow (paginated)")
            .WithTags("WorkflowEngine")
            .Produces<PagedResult<ExecutionSummaryResponse>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        workflowExecutions.MapPost("/", StartExecution)
            .RequireAuthorization(Permissions.Workflow.TriggerManual)
            .WithName("StartExecution")
            .WithSummary("Manually start a workflow execution")
            .WithTags("WorkflowEngine")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetAllExecutions(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ExecutionStatus? status = null)
    {

        PagedResult<ExecutionSummaryResponse> result = await mediator.Send(
            new GetAllExecutionsQuery(currentUser.TenantId, page, pageSize, status), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetExecutionsByWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ExecutionStatus? status = null)
    {

        PagedResult<ExecutionSummaryResponse> result = await mediator.Send(
            new GetExecutionsByWorkflowQuery(workflowId, currentUser.TenantId, page, pageSize, status), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetExecution(
        Guid executionId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        ExecutionResponse? result = await mediator.Send(
            new GetExecutionQuery(executionId, currentUser.TenantId), ct);
        if (result is null) return Results.NotFound();
        return Results.Ok(result);
    }

    private static async Task<IResult> StartExecution(
        Guid workflowId,
        [FromBody] StartExecutionRequest? request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new StartExecutionCommand(
            workflowId,
            currentUser.TenantId,
            TriggerType.Manual,
            currentUser.UserId,
            request?.Input), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/executions/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> CancelExecution(
        Guid executionId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new CancelExecutionCommand(executionId, currentUser.TenantId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> RetryExecution(
        Guid executionId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new RetryExecutionCommand(
            executionId, currentUser.TenantId, currentUser.UserId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/executions/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> RetryExecutionWithContext(
        Guid executionId,
        [FromBody] RetryExecutionWithContextRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new RetryExecutionWithContextCommand(
            executionId,
            currentUser.TenantId,
            currentUser.UserId,
            request.ModifiedContext), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/executions/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> GetRetryHistory(
        Guid executionId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        IReadOnlyList<ExecutionSummaryResponse> result = await mediator.Send(
            new GetRetryHistoryQuery(executionId, currentUser.TenantId), ct);
        return Results.Ok(result);
    }
}
