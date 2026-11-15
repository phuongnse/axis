using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The ready check of the control plane against a real region directory, see docs/reference/control-plane.md#health-and-telemetry.
/// Each test class instance has containers of its own, because a test stops the directory's.
/// </summary>
public sealed class DirectoryHealthEndpointTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _controlPlane = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PostgreSqlContainer _directory = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly LogCollector _logs = new();

    public async ValueTask InitializeAsync()
    {
        await _controlPlane.StartAsync(TestContext.Current.CancellationToken);
        await _directory.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _controlPlane.DisposeAsync();
        await _directory.DisposeAsync();
    }

    [Fact]
    public async Task Ready_answers_unhealthy_when_the_directory_is_stopped_and_names_the_directory_check_in_the_logs()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var healthy = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
        Assert.Equal("""{"status":"Healthy"}""", await healthy.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        await _directory.StopAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"Unhealthy"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        Assert.Contains(_logs.Entries, entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("directory", StringComparison.Ordinal)
            && entry.Message.Contains("The region directory database is unreachable.", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Entries, entry => entry.Message.Contains("The control plane database is unreachable.", StringComparison.Ordinal));
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var host = mailpit.SmtpHostAsync().GetAwaiter().GetResult();
        var port = mailpit.SmtpPortAsync().GetAwaiter().GetResult();
        var directoryConnectionString = new Npgsql.NpgsqlConnectionStringBuilder($"{_directory.GetConnectionString()};Timeout=2;Command Timeout=2")
        {
            Pooling = false,
        }.ConnectionString;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", $"{_controlPlane.GetConnectionString()};Timeout=2;Command Timeout=2");
            builder.UseSetting("ControlPlane:DirectoryConnectionString", directoryConnectionString);
            builder.UseSetting("ControlPlane:Dns:Provider", "InMemory");
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
        });
    }
}
