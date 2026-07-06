using System.Text.Json;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Api.Infrastructure;

internal static class ProblemDetailsDefaults
{
    public const string JsonContentType = "application/problem+json";
    public const string ValidationCode = "common.validation";
    public const string RateLimitedCode = "common.rateLimited";

    private const string ProblemTypePrefix = "urn:axis:problem:";

    public static string ToProblemType(string code) => $"{ProblemTypePrefix}{code}";

    public static string ToJsonFieldName(string fieldName) =>
        string.IsNullOrEmpty(fieldName)
            ? fieldName
            : JsonNamingPolicy.CamelCase.ConvertName(fieldName);

    public static string CodeFor(Result result) =>
        result.ProblemCode
        ?? result.ErrorCode switch
        {
            ErrorCodes.NotFound => "common.notFound",
            ErrorCodes.Forbidden => "common.forbidden",
            ErrorCodes.Conflict => "common.conflict",
            ErrorCodes.FieldValidation => ValidationCode,
            ErrorCodes.InvalidInput => "common.invalidInput",
            ErrorCodes.PlanLimit => "common.planLimit",
            ErrorCodes.RateLimited => RateLimitedCode,
            _ => "common.businessRule",
        };

    public static ProblemDetails CreateProblemDetails(
        int statusCode,
        string? detail,
        string code,
        string? title = null)
    {
        ProblemDetails problem = new()
        {
            Status = statusCode,
            Title = title ?? ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = detail,
            Type = ToProblemType(code),
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static HttpValidationProblemDetails CreateValidationProblemDetails(
        IReadOnlyDictionary<string, string[]> errors,
        int statusCode,
        string? detail,
        string code = ValidationCode,
        string? title = null,
        IReadOnlyDictionary<string, string[]>? errorCodes = null)
    {
        HttpValidationProblemDetails problem = new(errors.ToDictionary(kv => kv.Key, kv => kv.Value))
        {
            Status = statusCode,
            Title = title ?? ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = detail,
            Type = ToProblemType(code),
        };
        problem.Extensions["code"] = code;
        if (errorCodes is not null)
            problem.Extensions["errorCodes"] = errorCodes;

        return problem;
    }
}
