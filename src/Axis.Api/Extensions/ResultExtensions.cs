using Axis.Shared.Domain.Primitives;

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
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return result.ErrorCode switch
        {
            ErrorCodes.NotFound => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            ErrorCodes.Forbidden => Results.Problem(result.Error, statusCode: StatusCodes.Status403Forbidden),
            ErrorCodes.Conflict => Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
            ErrorCodes.PlanLimit when result.PlanLimitDetails is PlanLimitFailureDetails details =>
                Results.Json(
                    new
                    {
                        error = details.Error,
                        limit_type = details.LimitType,
                        current = details.Current,
                        max = details.Max,
                        upgrade_url = details.UpgradeUrl,
                        message = details.Message,
                    },
                    statusCode: StatusCodes.Status402PaymentRequired),
            ErrorCodes.PlanLimit => Results.Problem(result.Error, statusCode: StatusCodes.Status402PaymentRequired),
            ErrorCodes.InvalidInput => Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest),
            ErrorCodes.RateLimited => Results.Problem(result.Error, statusCode: StatusCodes.Status429TooManyRequests),
            _ => Results.Problem(result.Error, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }
}
