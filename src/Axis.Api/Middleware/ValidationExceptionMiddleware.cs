using FluentValidation;
using System.Text.Json;

namespace Axis.Api.Middleware;

internal sealed class ValidationExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            context.Response.ContentType = "application/json";

            var errors = (ex.Errors ?? [])
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            if (errors.Count == 0 && !string.IsNullOrEmpty(ex.Message))
                errors[""] = [ex.Message];

            var body = JsonSerializer.Serialize(
                new { error = "validation_failed", errors },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await context.Response.WriteAsync(body);
        }
    }
}
