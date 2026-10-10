using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

public sealed class TestUserTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static readonly string[] _testUsers =
    [
        "TestUsers:0:Id=anna",
        "TestUsers:0:DisplayName=Anna Employee",
        "TestUsers:0:Roles:0=employee",
        "TestUsers:1:Id=binh",
        "TestUsers:1:DisplayName=Binh Engineering Manager",
        "TestUsers:1:Roles:0=department-manager",
        "TestUsers:1:Roles:1=finance-reviewer",
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task After_sign_in_the_current_user_is_that_test_user()
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient();

        var signIn = await SignInAsync(client, "binh");
        var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        AssertUser(await ReadJsonAsync(signIn), "binh", "Binh Engineering Manager", "department-manager", "finance-reviewer");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        AssertUser(await ReadJsonAsync(me), "binh", "Binh Engineering Manager", "department-manager", "finance-reviewer");
    }

    [Fact]
    public async Task After_sign_out_nobody_is_signed_in()
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient();
        await SignInAsync(client, "anna");

        var signOut = await client.PostAsync(new Uri("/api/test-users/sign-out", UriKind.Relative), Json("{}"), CancellationToken);
        var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        await AssertNobodySignedInAsync(me);
    }

    [Fact]
    public async Task Without_a_cookie_nobody_is_signed_in()
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient();

        var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);

        await AssertNobodySignedInAsync(me);
    }

    [Theory]
    [InlineData("""{ "id": "zoe" }""")]
    [InlineData("""{ "id": "ANNA" }""")]
    [InlineData("""{ "id": null }""")]
    [InlineData("{}")]
    public async Task Sign_in_with_an_unknown_id_is_a_400_and_sets_no_cookie(string body)
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var signIn = await client.PostAsync(new Uri("/api/test-users/sign-in", UriKind.Relative), Json(body), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.False(signIn.Headers.Contains("Set-Cookie"));
        using var problem = await ReadJsonAsync(signIn);
        Assert.Equal(
            "Must be the id of a configured test user.",
            problem.RootElement.GetProperty("errors").GetProperty("id")[0].GetString());
        Assert.Equal("One or more validation errors occurred.", problem.RootElement.GetProperty("title").GetString());
        await AssertNobodySignedInAsync(await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken));
    }

    [Fact]
    public async Task Sign_in_with_a_body_that_is_not_json_is_a_400()
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var signIn = await client.PostAsync(new Uri("/api/test-users/sign-in", UriKind.Relative), Json("{ \"id\": "), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, signIn.StatusCode);
        Assert.Equal("application/problem+json", signIn.Content.Headers.ContentType?.MediaType);
        Assert.False(signIn.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/api/test-users/sign-in")]
    [InlineData("/api/test-users/sign-out")]
    public async Task A_post_that_is_not_json_is_a_415(string path)
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsync(
            new Uri(path, UriKind.Relative),
            new StringContent("""{ "id": "anna" }""", Encoding.UTF8, "text/plain"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task The_sign_in_cookie_is_http_only_and_same_site_strict()
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var signIn = await SignInAsync(client, "anna");

        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("Axis.TestUser=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        // The cookie holds a signed ticket, not the raw user id.
        Assert.DoesNotContain("Axis.TestUser=anna", cookie, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tampered_cookie_is_nobody_signed_in()
    {
        await using var factory = CreateFactory("Testing", _testUsers);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var cookie = Assert.Single((await SignInAsync(client, "anna")).Headers.GetValues("Set-Cookie"));
        var value = cookie[..cookie.IndexOf(';', StringComparison.Ordinal)];

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Add("Cookie", value[..^4] + "AAAA");
        var me = await client.SendAsync(request, CancellationToken);

        await AssertNobodySignedInAsync(me);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Startup_fails_when_test_users_are_configured_outside_development_and_testing(string environment)
    {
        await using var factory = CreateFactory(environment, _testUsers);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains($"'{environment}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_test_users_production_starts_with_no_sign_in_endpoints()
    {
        await using var factory = CreateFactory("Production", []);
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var signIn = await client.PostAsync(new Uri("/api/test-users/sign-in", UriKind.Relative), Json("""{ "id": "anna" }"""), CancellationToken);
        var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);

        Assert.DoesNotContain(endpoints, endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/test-users", StringComparison.Ordinal) == true);
        // Only the SPA fallback matches, and it takes GET and HEAD, as for any other path the server does not map.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, signIn.StatusCode);
        await AssertNobodySignedInAsync(me);
    }

    public static TheoryData<string[], string> InvalidTestUsers => new()
    {
        { ["TestUsers:0:Id=anna", "TestUsers:0:DisplayName=Anna", "TestUsers:1:Id=anna", "TestUsers:1:DisplayName=Anna again"], "Test user id 'anna' is configured more than once." },
        { ["TestUsers:0:Id= ", "TestUsers:0:DisplayName=Anna"], "Test user 0 has a blank id." },
        { ["TestUsers:0:Id=anna"], "Test user 0 has a blank display name." },
        { ["TestUsers:0:Id=anna", "TestUsers:0:DisplayName=Anna", "TestUsers:0:Roles:0= "], "Test user 0 has a blank role." },
    };

    [Theory]
    [MemberData(nameof(InvalidTestUsers))]
    public async Task Startup_fails_with_a_message_naming_the_test_user_problem(string[] settings, string problem)
    {
        await using var factory = CreateFactory("Testing", settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(problem, exception.Message, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string id) =>
        client.PostAsJsonAsync(new Uri("/api/test-users/sign-in", UriKind.Relative), new { id }, CancellationToken);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken);

    private static void AssertUser(JsonDocument body, string id, string displayName, params string[] roles)
    {
        using (body)
        {
            Assert.Equal(id, body.RootElement.GetProperty("id").GetString());
            Assert.Equal(displayName, body.RootElement.GetProperty("displayName").GetString());
            Assert.Equal(roles, body.RootElement.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!));
        }
    }

    private static async Task AssertNobodySignedInAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Nobody is signed in.", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, string[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            foreach (var setting in settings)
            {
                var separator = setting.IndexOf('=', StringComparison.Ordinal);
                builder.UseSetting(setting[..separator], setting[(separator + 1)..]);
            }
        });
}
