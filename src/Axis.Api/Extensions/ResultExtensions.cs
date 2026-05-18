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

        return result.ErrorCode switch
        {
            ErrorCodes.NotFound    => Results.Problem(result.Error, statusCode: StatusCodes.Status404NotFound),
            ErrorCodes.Conflict    => Results.Problem(result.Error, statusCode: StatusCodes.Status409Conflict),
            ErrorCodes.PlanLimit   => Results.Problem(result.Error, statusCode: StatusCodes.Status402PaymentRequired),
            _                      => Results.Problem(result.Error, statusCode: StatusCodes.Status422UnprocessableEntity),
        };
    }
}
