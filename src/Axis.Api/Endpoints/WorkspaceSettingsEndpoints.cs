using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Identity.Application.Commands.CancelWorkspaceDeletion;
using Axis.Identity.Application.Commands.ScheduleWorkspaceDeletion;
using Axis.Identity.Application.Commands.UpdateWorkspaceProfile;
using Axis.Identity.Application.Queries.GetWorkspaceSettings;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class WorkspaceSettingsEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/workspaces/current")
            .RequireAuthorization();

        group.MapGet("/settings", GetSettings)
            .RequireAuthorization(Permissions.Workspace.SettingsRead)
            .WithName("GetWorkspaceSettings")
            .WithSummary("Get workspace settings and usage stats")
            .WithTags("Identity")
            .Produces<WorkspaceSettingsDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/profile", UpdateProfile)
            .RequireAuthorization(Permissions.Workspace.SettingsWrite)
            .WithName("UpdateWorkspaceProfile")
            .WithSummary("Update workspace profile (name, timezone, language, logo)")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapPost("/deletion", ScheduleDeletion)
            .RequireAuthorization(Permissions.Workspace.Delete)
            .WithName("ScheduleWorkspaceDeletion")
            .WithSummary("Schedule workspace for deletion after a 30-day grace period")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        group.MapDelete("/deletion", CancelDeletion)
            .RequireAuthorization(Permissions.Workspace.Delete)
            .WithName("CancelWorkspaceDeletion")
            .WithSummary("Cancel a scheduled workspace deletion")
            .WithTags("Identity")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(422);

        return app;
    }

    private static async Task<IResult> GetSettings(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        WorkspaceSettingsDto? settings = await mediator.Send(
            new GetWorkspaceSettingsQuery(currentUser.WorkspaceId),
            ct);

        if (settings is null)
            return Results.NotFound();

        return Results.Ok(settings);
    }

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateWorkspaceProfileRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        byte[]? logoBytes = null;
        string? logoContentType = null;

        if (request.LogoBase64 is not null)
        {
            try
            {
                logoBytes = Convert.FromBase64String(request.LogoBase64);
            }
            catch (FormatException)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["logoBase64"] = ["Logo must be valid Base64-encoded data."],
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            logoContentType = request.LogoContentType ?? "image/png";
        }

        Result result = await mediator.Send(new UpdateWorkspaceProfileCommand(
            currentUser.WorkspaceId,
            request.Name,
            request.TimeZoneId,
            request.DefaultLanguage,
            logoBytes,
            logoContentType), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ScheduleDeletion(
        [FromBody] ScheduleWorkspaceDeletionRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new ScheduleWorkspaceDeletionCommand(
            currentUser.WorkspaceId,
            currentUser.UserId,
            request.ConfirmationName), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> CancelDeletion(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new CancelWorkspaceDeletionCommand(currentUser.WorkspaceId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}

public sealed record UpdateWorkspaceProfileRequest(
    string Name,
    string? TimeZoneId,
    string? DefaultLanguage,
    string? LogoBase64,
    string? LogoContentType);

public sealed record ScheduleWorkspaceDeletionRequest(string ConfirmationName);
