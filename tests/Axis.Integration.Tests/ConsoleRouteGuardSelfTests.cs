using Axis.Server.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// Runs <see cref="ConsoleRouteGuard"/> against a small server whose console routes break the rules on purpose, to show that
/// the guard reports exactly those routes. The server's operator is <c>pia</c> and <c>otto</c> is a user without the role.
/// </summary>
public sealed class ConsoleRouteGuardSelfTests
{
    private const string Host = "guard.example.test";

    private const string Marker = "guard-self-marker";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_route_with_no_role_check_is_reported_and_a_guarded_route_is_not()
    {
        await using var app = await StartAsync(app =>
        {
            app.MapGet("/api/console/unguarded", () => Results.Ok());
            Console(app).MapGet("/guarded", () => Results.Ok());
        });

        var failures = await AccessFailuresAsync(app);

        Assert.All(failures, failure => Assert.StartsWith("GET /api/console/unguarded ", failure, StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.StartsWith("GET /api/console/unguarded signed out -> 200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_write_that_accepts_a_request_without_the_csrf_header_is_reported_and_a_write_that_checks_it_is_not()
    {
        await using var app = await StartAsync(app =>
        {
            var console = Console(app);
            console.MapPost("/no-csrf", () => Results.Ok());
            console.MapPost("/with-csrf", (HttpRequest request) =>
                request.Headers["X-Axis-Csrf"] == "1" ? Results.Ok() : Results.StatusCode(StatusCodes.Status403Forbidden));
        });

        var failures = await AccessFailuresAsync(app);

        var failure = Assert.Single(failures);
        Assert.StartsWith("POST /api/console/no-csrf without the CSRF header -> 200", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_that_answers_a_seeded_value_is_reported_and_a_clean_read_is_not()
    {
        await using var app = await StartAsync(app =>
        {
            var console = Console(app);
            console.MapGet("/leaky/{id}", (string id) => Results.Json(new { value = Marker, id }));
            console.MapGet("/clean", () => Results.Json(new { }));
        });

        var failures = await ConsoleRouteGuard.LeakFailuresAsync(
            ConsoleRouteGuard.Routes(app.Services),
            app.GetTestClient,
            Host,
            "pia",
            new Dictionary<string, string> { ["id"] = "7" },
            [Marker],
            CancellationToken);

        var failure = Assert.Single(failures);
        Assert.StartsWith("GET /api/console/leaky/7 answered the seeded value", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_with_a_path_parameter_the_guard_cannot_fill_is_reported()
    {
        await using var app = await StartAsync(app =>
            Console(app).MapGet("/item/{id}", (string id) => id == "7" ? Results.Ok() : Results.NotFound()));

        var failures = await ConsoleRouteGuard.LeakFailuresAsync(
            ConsoleRouteGuard.Routes(app.Services),
            app.GetTestClient,
            Host,
            "pia",
            new Dictionary<string, string>(),
            [Marker],
            CancellationToken);

        var failure = Assert.Single(failures);
        Assert.StartsWith("GET /api/console/item/x answered 404, not 200", failure, StringComparison.Ordinal);
    }

    private static Task<IReadOnlyList<string>> AccessFailuresAsync(WebApplication app) =>
        ConsoleRouteGuard.AccessFailuresAsync(
            ConsoleRouteGuard.Routes(app.Services), app.GetTestClient, Host, "pia", "otto", CancellationToken);

    // A group that stands in for the operator check: 401 without a user and 403 unless the user is pia.
    private static RouteGroupBuilder Console(IEndpointRouteBuilder app)
    {
        var console = app.MapGroup("/api/console");
        console.AddEndpointFilter(async (context, next) =>
        {
            var user = context.HttpContext.Request.Headers[TestAuthentication.Header].ToString();
            if (user.Length == 0)
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            return user == "pia" ? await next(context) : Results.StatusCode(StatusCodes.Status403Forbidden);
        });
        return console;
    }

    private static async Task<WebApplication> StartAsync(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        map(app);
        await app.StartAsync(CancellationToken);
        return app;
    }
}
