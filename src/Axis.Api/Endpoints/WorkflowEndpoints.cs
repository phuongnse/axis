using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.AddStep;
using Axis.WorkflowBuilder.Application.Commands.AddTransition;
using Axis.WorkflowBuilder.Application.Commands.AddTrigger;
using Axis.WorkflowBuilder.Application.Commands.ArchiveWorkflow;
using Axis.WorkflowBuilder.Application.Commands.BulkExportWorkflows;
using Axis.WorkflowBuilder.Application.Commands.ConfigureStep;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Application.Commands.DeleteWorkflow;
using Axis.WorkflowBuilder.Application.Commands.DuplicateWorkflow;
using Axis.WorkflowBuilder.Application.Commands.ImportWorkflow;
using Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;
using Axis.WorkflowBuilder.Application.Commands.RemoveStep;
using Axis.WorkflowBuilder.Application.Commands.RemoveTransition;
using Axis.WorkflowBuilder.Application.Commands.RemoveTrigger;
using Axis.WorkflowBuilder.Application.Commands.UnarchiveWorkflow;
using Axis.WorkflowBuilder.Application.Commands.UpdateWorkflow;
using Axis.WorkflowBuilder.Application.Queries.ExportWorkflow;
using Axis.WorkflowBuilder.Application.Queries.GetWorkflow;
using Axis.WorkflowBuilder.Application.Queries.GetWorkflows;
using Axis.WorkflowBuilder.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/workflows")
            .RequireAuthorization();

        // ── Workflow CRUD ──────────────────────────────────────────────────────

        group.MapGet("/", GetWorkflows)
            .RequireAuthorization(Permissions.Workflow.DefinitionRead)
            .WithName("GetWorkflows")
            .WithSummary("List workflow definitions for the team account (paginated)")
            .WithTags("WorkflowBuilder")
            .Produces<PagedResult<WorkflowSummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("/", CreateWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("CreateWorkflow")
            .WithSummary("Create a new workflow definition in Draft status")
            .WithTags("WorkflowBuilder")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409);

        group.MapGet("/{workflowId:guid}", GetWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionRead)
            .WithName("GetWorkflow")
            .WithSummary("Get a workflow definition by ID")
            .WithTags("WorkflowBuilder")
            .Produces<WorkflowDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{workflowId:guid}", UpdateWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("UpdateWorkflow")
            .WithSummary("Update a workflow definition's name and description")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapDelete("/{workflowId:guid}", DeleteWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("DeleteWorkflow")
            .WithSummary("Permanently delete a Draft workflow")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        // ── Lifecycle ──────────────────────────────────────────────────────────

        group.MapPost("/{workflowId:guid}/publish", PublishWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("PublishWorkflow")
            .WithSummary("Validate and publish a workflow to Active status")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        group.MapPost("/{workflowId:guid}/archive", ArchiveWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("ArchiveWorkflow")
            .WithSummary("Archive a workflow, deactivating all its triggers")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        group.MapPost("/{workflowId:guid}/unarchive", UnarchiveWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("UnarchiveWorkflow")
            .WithSummary("Restore an archived workflow to Active status")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        group.MapPost("/{workflowId:guid}/duplicate", DuplicateWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("DuplicateWorkflow")
            .WithSummary("Create a full copy of a workflow in Draft status")
            .WithTags("WorkflowBuilder")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // ── Import / Export ────────────────────────────────────────────────────

        group.MapGet("/{workflowId:guid}/export", ExportWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionRead)
            .WithName("ExportWorkflow")
            .WithSummary("Export a workflow definition as a JSON file download")
            .WithTags("WorkflowBuilder")
            .Produces(200)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPost("/import", ImportWorkflow)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("ImportWorkflow")
            .WithSummary("Import a workflow from a previously exported JSON body")
            .WithTags("WorkflowBuilder")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapGet("/export-all", BulkExportWorkflows)
            .RequireAuthorization(Permissions.Workflow.DefinitionRead)
            .WithName("BulkExportWorkflows")
            .WithSummary("Export all workflow definitions as a ZIP archive")
            .WithTags("WorkflowBuilder")
            .Produces(200)
            .ProducesProblem(401)
            .ProducesProblem(403);

        // ── Steps ──────────────────────────────────────────────────────────────

        group.MapPost("/{workflowId:guid}/steps", AddStep)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("AddStep")
            .WithSummary("Add a typed step to a workflow definition")
            .WithTags("WorkflowBuilder")
            .Produces<CreatedResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{workflowId:guid}/steps/{stepId:guid}", ConfigureStep)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("ConfigureStep")
            .WithSummary("Update a step's name and configuration")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapDelete("/{workflowId:guid}/steps/{stepId:guid}", RemoveStep)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("RemoveStep")
            .WithSummary("Remove a step from a workflow definition")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        // ── Transitions ────────────────────────────────────────────────────────

        group.MapPost("/{workflowId:guid}/transitions", AddTransition)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("AddTransition")
            .WithSummary("Add a directed transition between two steps")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        group.MapDelete("/{workflowId:guid}/transitions", RemoveTransition)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("RemoveTransition")
            .WithSummary("Remove a transition between two steps")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        // ── Triggers ───────────────────────────────────────────────────────────

        group.MapPost("/{workflowId:guid}/triggers", AddTrigger)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("AddTrigger")
            .WithSummary("Add a trigger to a workflow definition")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapDelete("/{workflowId:guid}/triggers/{triggerType}", RemoveTrigger)
            .RequireAuthorization(Permissions.Workflow.DefinitionWrite)
            .WithName("RemoveTrigger")
            .WithSummary("Remove a trigger from a workflow definition")
            .WithTags("WorkflowBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422);

        return app;
    }

    // ── Handlers ───────────────────────────────────────────────────────────────

    private static async Task<IResult> GetWorkflows(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        PagedResult<WorkflowSummaryDto> result = await mediator.Send(
            new GetWorkflowsQuery(currentUser.TeamAccountId, page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateWorkflow(
        [FromBody] CreateWorkflowRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(
            new CreateWorkflowCommand(request.Name, request.Description, currentUser.TeamAccountId, currentUser.UserId.ToString()), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/workflows/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> GetWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        WorkflowDetailDto? dto = await mediator.Send(
            new GetWorkflowQuery(workflowId, currentUser.TeamAccountId), ct);
        if (dto is null)
            return Results.Problem($"Workflow '{workflowId}' not found.", statusCode: StatusCodes.Status404NotFound);
        return Results.Ok(dto);
    }

    private static async Task<IResult> UpdateWorkflow(
        Guid workflowId,
        [FromBody] UpdateWorkflowRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new UpdateWorkflowCommand(workflowId, currentUser.TeamAccountId, request.Name, request.Description), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> PublishWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new PublishWorkflowCommand(workflowId, currentUser.TeamAccountId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ArchiveWorkflowCommand(workflowId, currentUser.TeamAccountId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> UnarchiveWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new UnarchiveWorkflowCommand(workflowId, currentUser.TeamAccountId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new DeleteWorkflowCommand(workflowId, currentUser.TeamAccountId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> DuplicateWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(
            new DuplicateWorkflowCommand(workflowId, currentUser.TeamAccountId, currentUser.UserId.ToString()), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/workflows/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> ExportWorkflow(
        Guid workflowId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        WorkflowExportDto? dto = await mediator.Send(
            new ExportWorkflowQuery(workflowId, currentUser.TeamAccountId), ct);
        if (dto is null)
            return Results.Problem($"Workflow '{workflowId}' not found.", statusCode: StatusCodes.Status404NotFound);

        string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        string slug = ToSafeSlug(dto.Name);
        string date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        string fileName = $"{slug}-{date}.json";

        return Results.File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    private static async Task<IResult> ImportWorkflow(
        [FromBody] WorkflowExportDto exportData,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(
            new ImportWorkflowCommand(currentUser.TeamAccountId, currentUser.UserId.ToString(), exportData), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/workflows/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> BulkExportWorkflows(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        IReadOnlyList<WorkflowExportDto> workflows = await mediator.Send(
            new BulkExportWorkflowsCommand(currentUser.TeamAccountId), ct);

        JsonSerializerOptions jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        using MemoryStream zipStream = new();
        using (ZipArchive zip = new(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Dictionary<string, int> seen = new(StringComparer.Ordinal);
            int idx = 0;
            foreach (WorkflowExportDto dto in workflows)
            {
                string baseSlug = ToSafeSlug(dto.Name);
                if (string.IsNullOrEmpty(baseSlug)) baseSlug = $"workflow-{idx}";
                idx++;
                seen.TryGetValue(baseSlug, out int count);
                string entrySlug = count == 0 ? baseSlug : $"{baseSlug}_{count + 1}";
                seen[baseSlug] = count + 1;

                ZipArchiveEntry entry = zip.CreateEntry($"{entrySlug}.json");
                await using Stream entryStream = entry.Open();
                await JsonSerializer.SerializeAsync(entryStream, dto, jsonOpts, ct);
            }
        }

        string date = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        return Results.File(zipStream.ToArray(), "application/zip", $"workflows-{date}.zip");
    }

    private static async Task<IResult> AddStep(
        Guid workflowId,
        [FromBody] AddStepRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(
            new AddStepCommand(workflowId, currentUser.TeamAccountId, request.Name, request.StepType, request.Config), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/workflows/{workflowId}/steps/{result.Value}", new CreatedResponse(result.Value));
    }

    private static async Task<IResult> ConfigureStep(
        Guid workflowId,
        Guid stepId,
        [FromBody] ConfigureStepRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ConfigureStepCommand(workflowId, currentUser.TeamAccountId, stepId, request.Name, request.Config), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveStep(
        Guid workflowId,
        Guid stepId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new RemoveStepCommand(workflowId, currentUser.TeamAccountId, stepId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> AddTransition(
        Guid workflowId,
        [FromBody] AddTransitionRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new AddTransitionCommand(workflowId, currentUser.TeamAccountId, request.FromStepId, request.ToStepId, request.Label), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveTransition(
        Guid workflowId,
        [FromBody] RemoveTransitionRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new RemoveTransitionCommand(workflowId, currentUser.TeamAccountId, request.FromStepId, request.ToStepId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> AddTrigger(
        Guid workflowId,
        [FromBody] AddTriggerRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new AddTriggerCommand(workflowId, currentUser.TeamAccountId, request.TriggerType, request.Config), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveTrigger(
        Guid workflowId,
        string triggerType,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        if (!Enum.TryParse<TriggerType>(triggerType, ignoreCase: true, out TriggerType parsedType))
        {
            return Result.Failure(ErrorCodes.InvalidInput, $"Unknown trigger type: '{triggerType}'.")
                .ToProblemDetails();
        }

        Result result = await mediator.Send(
            new RemoveTriggerCommand(workflowId, currentUser.TeamAccountId, parsedType), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    // Produces a filename-safe slug: lowercase, spaces → hyphens, non-alphanumeric/hyphen chars stripped.
    private static string ToSafeSlug(string name)
    {
        string slug = name.ToLowerInvariant().Replace(' ', '-');
        slug = new string(slug.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return slug.Trim('-', '_');
    }
}
