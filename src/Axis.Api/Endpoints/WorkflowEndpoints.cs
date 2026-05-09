using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;
using Axis.WorkflowBuilder.Application.Queries.GetWorkflows;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflows")
            .RequireAuthorization();

        group.MapGet("/", GetWorkflows)
            .RequireAuthorization(Permissions.Workflow.DefinitionRead);

        group.MapPost("/", CreateWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite);

        group.MapPost("/{workflowId:guid}/publish", PublishWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite);

        return app;
    }

    private static async Task<IResult> GetWorkflows(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetWorkflowsQuery(currentUser.OrgId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateWorkflow(
        [FromBody] CreateWorkflowRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        var id = await mediator.Send(new CreateWorkflowCommand(
            request.Name,
            request.Description,
            currentUser.OrgId), ct);

        return Results.Created($"/api/workflows/{id}", new { id });
    }

    private static async Task<IResult> PublishWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new PublishWorkflowCommand(workflowId, currentUser.OrgId), ct);
        return Results.NoContent();
    }
}

public record CreateWorkflowRequest(string Name, string? Description);
