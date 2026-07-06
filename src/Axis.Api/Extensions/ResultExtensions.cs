using System.Text.Json;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Api.Extensions;

/// <summary>
/// Maps a failed Result to an RFC 7807 ProblemDetails HTTP response.
/// Status code is determined by Result.ErrorCode — see ErrorCodes for the mapping table.
/// </summary>
public static class ResultExtensions
{
    private const string ProblemJsonContentType = "application/problem+json";
    private const string ProblemTypePrefix = "urn:axis:problem:";

    public static IResult ToProblemDetails(this Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("Cannot convert a successful result to ProblemDetails.");

        if (result.ErrorCode == ErrorCodes.FieldValidation && result.FieldErrors is not null)
        {
            Dictionary<string, string[]> errors = result.FieldErrors
                .ToDictionary(kv => ToJsonFieldName(kv.Key), kv => kv.Value);
            return Problem(
                result,
                StatusCodes.Status422UnprocessableEntity,
                validationErrors: errors);
        }

        if (result.ErrorCode == ErrorCodes.PlanLimit
            && result.PlanLimitDetails is PlanLimitFailureDetails details)
        {
            return Results.Json(
                new
                {
                    error = details.Error,
                    limit_type = details.LimitType,
                    current = details.Current,
                    max = details.Max,
                    upgrade_url = details.UpgradeUrl,
                    message = details.Message,
                },
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        int statusCode = result.ErrorCode switch
        {
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.Conflict => StatusCodes.Status409Conflict,
            ErrorCodes.PlanLimit => StatusCodes.Status402PaymentRequired,
            ErrorCodes.InvalidInput => StatusCodes.Status400BadRequest,
            ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        return Problem(result, statusCode);
    }

    private static IResult Problem(
        Result result,
        int statusCode,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        string code = GetProblemCode(result);
        ProblemDetails problem = validationErrors is null
            ? new ProblemDetails()
            : new HttpValidationProblemDetails(
                validationErrors.ToDictionary(kv => kv.Key, kv => kv.Value));
        problem.Status = statusCode;
        problem.Title = ReasonPhrases.GetReasonPhrase(statusCode);
        problem.Detail = result.Error;
        problem.Type = $"{ProblemTypePrefix}{code}";
        problem.Extensions["code"] = code;

        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: ProblemJsonContentType);
    }

    private static string GetProblemCode(Result result) =>
        result.ProblemCode
        ?? result.ErrorCode switch
        {
            ErrorCodes.NotFound => "common.notFound",
            ErrorCodes.Forbidden => "common.forbidden",
            ErrorCodes.Conflict => "common.conflict",
            ErrorCodes.FieldValidation => "common.validation",
            ErrorCodes.InvalidInput => "common.invalidInput",
            ErrorCodes.PlanLimit => "common.planLimit",
            ErrorCodes.RateLimited => "common.rateLimited",
            _ => "common.businessRule",
        };

    private static string ToJsonFieldName(string fieldName) =>
        string.IsNullOrEmpty(fieldName)
            ? fieldName
            : JsonNamingPolicy.CamelCase.ConvertName(fieldName);
}
