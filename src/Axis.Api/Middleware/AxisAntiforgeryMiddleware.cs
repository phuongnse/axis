using Axis.Api.Endpoints;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

internal sealed class AxisAntiforgeryMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<WorkspaceTransitionRecoveryMetadata>() is not null
            && context.User.Identity?.IsAuthenticated != true)
        {
            AuthenticateResult transition = await context.AuthenticateAsync(
                AxisApiServiceExtensions.WorkspaceTransitionScheme);
            if (transition.Succeeded && transition.Principal is not null)
                context.User = transition.Principal;
        }

        bool axisApiMutation = context.Request.Path.StartsWithSegments("/api") &&
            !SafeMethods.Contains(context.Request.Method);
        bool hasAuthorizationHeader = context.Request.Headers.ContainsKey("Authorization");
        if (axisApiMutation && !hasAuthorizationHeader)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                const int statusCode = StatusCodes.Status400BadRequest;
                ProblemDetails problem = ProblemDetailsDefaults.CreateProblemDetails(
                    statusCode,
                    "The request could not be validated.",
                    "identity.invalidAntiforgery",
                    "Invalid request");
                await Results.Json(
                    problem,
                    statusCode: statusCode,
                    contentType: ProblemDetailsDefaults.JsonContentType)
                    .ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
