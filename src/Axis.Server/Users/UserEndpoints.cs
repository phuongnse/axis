using System.Security.Claims;
using System.Text.Json;
using Axis.Server.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Axis.Server.Users;

/// <summary>
/// The current user, and the sign-in endpoints of the development test users. The sign-in cookie
/// holds a signed ticket with the user id. The name and roles are read from the configured list on
/// every request, so an id that is no longer configured is nobody signed in.
/// A sign-in body is checked in order: content type (415), then body (400), then the id (400). The
/// problem titles never contain text from the request.
/// </summary>
internal static class UserEndpoints
{
    public static void MapCurrentUserEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/me", GetCurrentUser);

    public static void MapTestUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var testUsers = endpoints.MapGroup("/api/test-users");
        testUsers.MapPost("/sign-in", SignInAsync);
        testUsers.MapPost("/sign-out", SignOutAsync);
    }

    private static IResult GetCurrentUser(HttpContext httpContext, TestUserDirectory directory) =>
        httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id && directory.Find(id) is { } user
            ? Results.Ok(ToResponse(user))
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nobody is signed in.");

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
