using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

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
        { With("Profile", "other"), "Tenant 'default' has Oidc profile 'other': use 'standard' or 'fapi2'." },
        { With("Profile", "FAPI2"), "Tenant 'default' has Oidc profile 'FAPI2': use 'standard' or 'fapi2'." },
        { Fapi2With(), "Tenant 'default' has no Oidc client key." },
        { Fapi2With($"ClientKey={NewPem()}", "ClientKeyId= "), "Tenant 'default' has an Oidc client key without a key id." },
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
            $"Tenants:b:SchemaConnectionString={UnreachableDatabase}",
            "Tenants:c:Hosts:0=c.example.test",
            $"Tenants:c:ConnectionString={UnreachableDatabase}",
            $"Tenants:c:SchemaConnectionString={UnreachableDatabase}",
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

    [Fact]
    public async Task An_explicit_standard_profile_starts_like_an_unset_one()
    {
        await using var factory = CreateFactory(With("Profile", "standard"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_standard_tenant_with_a_client_key_but_no_client_secret_does_not_start()
    {
        await using var factory = CreateFactory([.. Without("ClientSecret"), $"Tenants:default:Oidc:ClientKey={NewPem()}", "Tenants:default:Oidc:ClientKeyId=key-1"]);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Tenant 'default' has no Oidc client secret.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fapi2_tenant_with_a_client_key_starts_without_a_client_secret()
    {
        var pem = NewPem();
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(Fapi2With($"ClientKey={pem}", "ClientKeyId=key-1"), logs);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(logs.Lines);
        Assert.All(logs.Lines, line => Assert.DoesNotContain(pem, line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_startup_failure_of_a_fapi2_tenant_does_not_show_its_client_key()
    {
        var pem = NewPem();
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory(
            [.. Fapi2With($"ClientKey={pem}", "ClientKeyId=key-1").Where(setting => !setting.StartsWith("Tenants:default:Oidc:Authority=", StringComparison.Ordinal)), "Tenants:default:Oidc:Authority= "],
            logs);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Tenant 'default' has no Oidc authority.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(pem, exception.ToString(), StringComparison.Ordinal);
        Assert.All(logs.Lines, line => Assert.DoesNotContain(pem, line, StringComparison.Ordinal));
    }

    // The test key is generated at run time, so no key is committed.
    private static string NewPem()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return key.ExportPkcs8PrivateKeyPem();
    }

    // A fapi2 tenant without a client secret. Each extra setting names a key under Oidc and may replace one.
    private static string[] Fapi2With(params string[] settings) =>
    [
        .. Without("ClientSecret"),
        "Tenants:default:Oidc:Profile=fapi2",
        .. settings.Select(setting => $"Tenants:default:Oidc:{setting}"),
    ];

    private static string[] Without(string key) =>
        [.. _completeOidc.Where(setting => !setting.StartsWith($"Tenants:default:Oidc:{key}=", StringComparison.Ordinal))];

    private static string[] With(string key, string value) =>
        [.. Without(key), $"Tenants:default:Oidc:{key}={value}"];

    private static WebApplicationFactory<Program> CreateFactory(string[] settings, CapturingLoggerProvider? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }

            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            foreach (var setting in settings)
            {
                var separator = setting.IndexOf('=', StringComparison.Ordinal);
                builder.UseSetting(setting[..separator], setting[(separator + 1)..]);
            }
        });

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Lines.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
