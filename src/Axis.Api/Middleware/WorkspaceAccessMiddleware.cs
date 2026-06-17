using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Rejects workspace-scoped API calls when the JWT <c>workspace_id</c> references a missing,
/// suspended, or not-yet-ready Workspace.
/// </summary>
internal sealed class WorkspaceAccessMiddleware(RequestDelegate next)
{
    private const string WorkspaceIdClaim = "workspace_id";

    public async Task InvokeAsync(
        HttpContext context,
        IWorkspaceAccessService accessService)
    {
        if (!WorkspaceDataApiPaths.RequiresWorkspaceDataAccess(context.Request.Path)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? WorkspaceIdValue = context.User.FindFirst(WorkspaceIdClaim)?.Value;
        if (string.IsNullOrEmpty(WorkspaceIdValue) || !Guid.TryParse(WorkspaceIdValue, out Guid workspaceId))
        {
            await next(context);
            return;
        }

        Result access = await accessService.EvaluateAsync(workspaceId, context.RequestAborted);

        if (access.IsSuccess)
        {
            await next(context);
            return;
        }

        ProblemDetails problem = new()
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Forbidden",
            Detail = access.Error,
        };

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
