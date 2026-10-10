using System.Security.Claims;
using System.Text.Json;
using Axis.Server.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Axis.Server.Users;

/// <summary>
/// The current user, the list of development test users, and their sign-in endpoints. The list is
/// mapped in every environment and answers a 404 problem where test users are not allowed, so the
/// request does not reach the SPA fallback. The sign-in cookie
/// holds a signed ticket with the user id. The name and roles are read from the configured list on
/// every request, so an id that is no longer configured is nobody signed in.
/// A sign-in body is checked in order: content type (415), then body (400), then the id (400). The
/// problem titles never contain text from the request.
/// </summary>
internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me", GetCurrentUser);
        endpoints.MapGet("/api/test-users", GetTestUsers);
    }

    public static void MapTestUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var testUsers = endpoints.MapGroup("/api/test-users");
        testUsers.MapPost("/sign-in", SignInAsync);
        testUsers.MapPost("/sign-out", SignOutAsync);
    }

    private static IResult GetCurrentUser(HttpContext httpContext, TestUserDirectory directory) =>
        directory.FindSignedIn(httpContext.User) is { } id && directory.Find(id) is { } user
            ? Results.Ok(ToResponse(user))
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nobody is signed in.");

    private static IResult GetTestUsers(TestUserDirectory directory) =>
        directory.Enabled
            ? Results.Ok(new TestUserListResponse([.. directory.Users.Select(ToResponse)]))
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Test users are not available.");

    // The body is read here rather than bound, so the content type is checked before any of it is read.
    private static async Task<IResult> SignInAsync(HttpContext httpContext, TestUserDirectory directory, CancellationToken cancellationToken)
    {
        if (!JsonRequest.IsJson(httpContext.Request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        SignInRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SignInRequest>(httpContext.Request.Body, JsonSerializerOptions.Web, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request body is not valid JSON.");
        }

        if (body?.Id is not { } id || directory.Find(id) is not { } user)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["id"] = ["Must be the id of a configured test user."],
            });
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> SignOutAsync(HttpRequest request)
    {
        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        await request.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static CurrentUserResponse ToResponse(TestUser user) => new(user.Id, user.DisplayName, user.Roles);

    private sealed record SignInRequest(string? Id);
}

/// <summary>The signed-in user's id, display name and role names.</summary>
internal sealed record CurrentUserResponse(string Id, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>The configured test users in configuration order.</summary>
internal sealed record TestUserListResponse(IReadOnlyList<CurrentUserResponse> Users);
