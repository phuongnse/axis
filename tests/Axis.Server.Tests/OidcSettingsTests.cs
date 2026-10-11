using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class OidcSettingsTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static readonly string[] _completeOidc =
    [
        "Tenants:default:Oidc:Authority=https://127.0.0.1:1/realms/axis",
        "Tenants:default:Oidc:ClientId=axis",
        "Tenants:default:Oidc:ClientSecret=test",
    ];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string[], string> InvalidOidc => new()
    {
        { [], "Tenant 'default' has no Oidc block." },
        { Without("Authority"), "Tenant 'default' has no Oidc authority." },
        { With("Authority", " "), "Tenant 'default' has no Oidc authority." },
        { Without("ClientId"), "Tenant 'default' has no Oidc client id." },
        { With("ClientId", ""), "Tenant 'default' has no Oidc client id." },
        { Without("ClientSecret"), "Tenant 'default' has no Oidc client secret." },
        { With("ClientSecret", " "), "Tenant 'default' has no Oidc client secret." },
    };

    [Theory]
    [MemberData(nameof(InvalidOidc))]
    public async Task Startup_fails_with_a_message_naming_the_tenant_and_the_sign_in_problem(string[] settings, string problem)
    {
        await using var factory = CreateFactory(settings);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("The sign-in configuration is invalid.", exception.Message, StringComparison.Ordinal);
        Assert.Contains(problem, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_names_every_tenant_with_a_problem()
    {
        await using var factory = CreateFactory(
        [
            .. _completeOidc,
            "Tenants:b:Hosts:0=b.example.test",
            $"Tenants:b:ConnectionString={UnreachableDatabase}",
            "Tenants:c:Hosts:0=c.example.test",
            $"Tenants:c:ConnectionString={UnreachableDatabase}",
            "Tenants:c:Oidc:ClientId=axis",
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Tenant 'b' has no Oidc block. Tenant 'c' has no Oidc authority. Tenant 'c' has no Oidc client secret.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'default'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_complete_block_starts_the_server()
    {
        await using var factory = CreateFactory(_completeOidc);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string[] Without(string key) =>
        [.. _completeOidc.Where(setting => !setting.StartsWith($"Tenants:default:Oidc:{key}=", StringComparison.Ordinal))];

    private static string[] With(string key, string value) =>
        [.. Without(key), $"Tenants:default:Oidc:{key}={value}"];

    private static WebApplicationFactory<Program> CreateFactory(string[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
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
