using System.Net;
using Axis.ControlPlane.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The control plane against a real PostgreSQL. Each test class instance has a container of its own, because a test stops it.
/// </summary>
public sealed class ControlPlaneDatabaseTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task The_start_applies_the_migrations_and_ready_answers_healthy()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"Healthy"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var applied = await AppliedMigrationsAsync();
        Assert.Contains(applied, migration => migration.EndsWith("_CreateControlPlaneSchema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ready_answers_unhealthy_when_the_database_is_stopped()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var healthy = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);

        await _container.StopAsync(TestContext.Current.CancellationToken);

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"Unhealthy"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Ready_answers_unhealthy_when_the_mail_relay_does_not_accept_a_connection_and_live_stays_healthy()
    {
        using var factory = CreateFactory(mailPort: 1);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"Unhealthy"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private string ConnectionString => $"{_container.GetConnectionString()};Timeout=2;Command Timeout=2";

    private WebApplicationFactory<Program> CreateFactory(int? mailPort = null)
    {
        var host = mailpit.SmtpHostAsync().GetAwaiter().GetResult();
        var port = mailPort ?? mailpit.SmtpPortAsync().GetAwaiter().GetResult();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", ConnectionString);
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
        });
    }

    private async Task<IReadOnlyList<string>> AppliedMigrationsAsync()
    {
        var options = new DbContextOptionsBuilder<ControlPlaneDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString)
            .Options;
        await using var context = new ControlPlaneDbContext(options);
        return [.. await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)];
    }
}
