using Axis.Api.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

internal sealed class AxisAntiforgeryMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
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
