namespace Axis.Server.Http;

/// <summary>
/// The problems of a request that is not allowed. A request without a session is a 401, and one
/// whose user holds no grant for it is a 403. Each has one fixed title for every route, which names
/// no application, entity, data source or action.
/// </summary>
internal static class Grants
{
    public static IResult SignInRequired() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in to read or write this data.");

    public static IResult Forbidden() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You have no grant for this action.");
}
