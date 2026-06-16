using Axis.Api.Infrastructure;
using Axis.Identity.Application.Services;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Rejects tenant-scoped API calls when the JWT <c>team_account_id</c> references a missing,
/// suspended, or not-yet-ready teamAccount.
/// </summary>
internal sealed class TenantTeamAccountAccessMiddleware(RequestDelegate next)
{
    private const string TeamAccountIdClaim = "team_account_id";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantTeamAccountAccessService accessService)
    {
        if (!TenantDataApiPaths.RequiresTenantDataAccess(context.Request.Path)
            || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        string? teamAccountIdValue = context.User.FindFirst(TeamAccountIdClaim)?.Value;
        if (string.IsNullOrEmpty(teamAccountIdValue) || !Guid.TryParse(teamAccountIdValue, out Guid teamAccountId))
        {
            await next(context);
            return;
        }

        Result access = await accessService.EvaluateAsync(teamAccountId, context.RequestAborted);

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
