using Axis.Api.Infrastructure;
using Axis.Shared.Domain.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Extensions;

/// <summary>
/// Maps a failed Result to an RFC 7807 ProblemDetails HTTP response.
/// Status code is determined by Result.ErrorCode — see ErrorCodes for the mapping table.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToProblemDetails(this Result result)
    {
        if (result.IsSuccess)
            throw new InvalidOperationException("Cannot convert a successful result to ProblemDetails.");

        if (result.ErrorCode == ErrorCodes.FieldValidation && result.FieldErrors is not null)
        {
            Dictionary<string, string[]> errors = result.FieldErrors
                .ToDictionary(kv => ProblemDetailsDefaults.ToJsonFieldName(kv.Key), kv => kv.Value);
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
        string code = ProblemDetailsDefaults.CodeFor(result);
        ProblemDetails problem = validationErrors is null
            ? ProblemDetailsDefaults.CreateProblemDetails(statusCode, result.Error, code)
            : ProblemDetailsDefaults.CreateValidationProblemDetails(
                validationErrors,
                statusCode,
                result.Error,
                code);

        return Results.Json(
            problem,
            statusCode: statusCode,
            contentType: ProblemDetailsDefaults.JsonContentType);
    }
}
