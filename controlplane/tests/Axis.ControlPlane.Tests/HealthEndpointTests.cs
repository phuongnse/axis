using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Axis.ControlPlane.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Live_answers_healthy_without_the_database()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"status":"Healthy"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Ready_answers_unhealthy_when_the_database_is_unreachable_and_names_no_error()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("""{"status":"Unhealthy"}""", body);
    }

    [Fact]
    public async Task Ready_runs_no_directory_check_without_the_directory_setting()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Equal(["database", "mail-relay"], registrations.Select(registration => registration.Name).Order(StringComparer.Ordinal));
        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"Unhealthy"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Ready_runs_the_directory_check_when_the_directory_is_set()
    {
        using var factory = ControlPlaneFactory.Create(settings: new Dictionary<string, string?>
        {
            ["ControlPlane:DirectoryConnectionString"] = ControlPlaneFactory.UnreachableConnectionString,
            ["ControlPlane:Dns:Provider"] = "InMemory",
        });

        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var directory = Assert.Single(registrations, registration => registration.Name == "directory");
        Assert.Contains("ready", directory.Tags);
    }
}
