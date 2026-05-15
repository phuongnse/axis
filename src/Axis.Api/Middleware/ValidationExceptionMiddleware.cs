using FluentValidation;

namespace Axis.Api.Middleware;

/// <summary>
/// Catches FluentValidation.ValidationException bubbled up by ValidationBehavior
/// and converts it to an RFC 7807 ProblemDetails response (HTTP 422).
/// </summary>
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
            Dictionary<string, string[]> errors = (ex.Errors ?? [])
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            if (errors.Count == 0 && !string.IsNullOrEmpty(ex.Message))
                errors[""] = [ex.Message];

            await Results.ValidationProblem(
                errors,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "One or more validation errors occurred.")
                .ExecuteAsync(context);
        }
    }
}
