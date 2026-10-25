using Axis.Server.Authentication;

namespace Axis.Server.Http;

/// <summary>
/// The problems of a request that is not allowed. A request without a session is a 401, and one
/// whose user holds no grant for it is a 403, as is a write to a record outside the user's filters
/// for the action and a write of a field no grant for the action covers. A request whose access depends on a field that is being recomputed is a 409. Each has one fixed title for every route, which names no application, entity,
/// data source, action or filter.
/// </summary>
internal static class Grants
{
    public static IResult SignInRequired() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign in to read or write this data.");

    /// <summary>
    /// The 401 of a request. A service account request, which has a bearer token, gets <c>WWW-Authenticate: Bearer</c>
    /// and its own title. Any other request gets the sign-in problem.
    /// </summary>
    public static IResult SignInRequired(HttpContext context)
    {
        if (!ServiceAccount.Applies(context))
        {
            return SignInRequired();
        }

        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "A valid bearer token is needed for this request.");
    }

    public static IResult Forbidden() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "You have no grant for this action.");

    public static IResult OutsideFilter() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The record is outside your grants for this action.");

    /// <summary>
    /// A request whose access is decided by a filter that reads a computed field that is being recomputed.
    /// It names no field, policy or filter, and has no <c>errors</c>.
    /// </summary>
    public static IResult PendingFilter() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Your access to this entity cannot be checked while a field is being recomputed.");

    /// <summary>A write of fields no grant covers, with an error at the path of each of them.</summary>
    public static IResult UnwritableFields(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, statusCode: StatusCodes.Status403Forbidden, title: "You have no grant to write these fields.");
}
