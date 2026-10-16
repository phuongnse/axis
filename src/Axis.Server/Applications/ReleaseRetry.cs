namespace Axis.Server.Applications;

/// <summary>
/// The bound on how often a request runs again after the active release changed under the start
/// lock, and the answer when the bound is reached. Nothing was written by the attempts.
/// </summary>
internal static class ReleaseRetry
{
    /// <summary>The attempts a request makes before it answers <c>503</c>.</summary>
    public const int MaxAttempts = 3;

    /// <summary>The <c>503</c> for a request whose release changed on every attempt.</summary>
    public static IResult Changed() =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "The application's active release changed while the request ran. Try again.");
}
