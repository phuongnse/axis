using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Middleware;

/// <summary>
/// Catches FluentValidation.ValidationException bubbled up by ValidationBehavior
/// and converts it to an RFC 7807 ProblemDetails response (HTTP 400).
/// </summary>
internal sealed class ValidationExceptionMiddleware(RequestDelegate next)
{
    private const string ProblemJsonContentType = "application/problem+json";
    private const string ValidationProblemCode = "common.validation";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            Dictionary<string, string[]> errors = (ex.Errors ?? [])
                .GroupBy(e => ToJsonFieldName(e.PropertyName))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());
            Dictionary<string, string[]> errorCodes = (ex.Errors ?? [])
                .GroupBy(e => ToJsonFieldName(e.PropertyName))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => string.IsNullOrWhiteSpace(e.ErrorCode)
                        ? ValidationProblemCode
                        : e.ErrorCode).ToArray());

            if (errors.Count == 0 && !string.IsNullOrEmpty(ex.Message))
            {
                errors[""] = [ex.Message];
                errorCodes[""] = [ValidationProblemCode];
            }

            const int statusCode = StatusCodes.Status400BadRequest;
            HttpValidationProblemDetails problem = new(errors)
            {
                Status = statusCode,
                Title = "One or more validation errors occurred.",
                Type = $"urn:axis:problem:{ValidationProblemCode}",
            };
            problem.Extensions["code"] = ValidationProblemCode;
            problem.Extensions["errorCodes"] = errorCodes;

            await Results.Json(
                problem,
                statusCode: statusCode,
                contentType: ProblemJsonContentType)
                .ExecuteAsync(context);
        }
    }

    private static string ToJsonFieldName(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : JsonNamingPolicy.CamelCase.ConvertName(propertyName);
}
