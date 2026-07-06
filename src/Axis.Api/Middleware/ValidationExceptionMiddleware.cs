using Axis.Api.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Catches FluentValidation.ValidationException bubbled up by ValidationBehavior
/// and converts it to an RFC 7807 ProblemDetails response (HTTP 400).
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
                .GroupBy(e => ProblemDetailsDefaults.ToJsonFieldName(e.PropertyName))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());
            Dictionary<string, string[]> errorCodes = (ex.Errors ?? [])
                .GroupBy(e => ProblemDetailsDefaults.ToJsonFieldName(e.PropertyName))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => string.IsNullOrWhiteSpace(e.ErrorCode)
                        ? ProblemDetailsDefaults.ValidationCode
                        : e.ErrorCode).ToArray());

            if (errors.Count == 0 && !string.IsNullOrEmpty(ex.Message))
            {
                errors[""] = [ex.Message];
                errorCodes[""] = [ProblemDetailsDefaults.ValidationCode];
            }

            const int statusCode = StatusCodes.Status400BadRequest;
            HttpValidationProblemDetails problem = ProblemDetailsDefaults.CreateValidationProblemDetails(
                errors,
                statusCode,
                detail: null,
                code: ProblemDetailsDefaults.ValidationCode,
                title: "One or more validation errors occurred.",
                errorCodes: errorCodes);

            await Results.Json(
                problem,
                statusCode: statusCode,
                contentType: ProblemDetailsDefaults.JsonContentType)
                .ExecuteAsync(context);
        }
    }
}
