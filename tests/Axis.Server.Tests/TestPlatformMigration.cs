using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Server.Tests;

/// <summary>Removes the platform migration step, so a test host starts without a platform database.</summary>
internal static class TestPlatformMigration
{
    /// <summary>
    /// Removes the hosted service that applies the platform migrations under <c>Tenancy:Source=configuration</c>.
    /// It runs after the server's own registrations.
    /// </summary>
    public static IWebHostBuilder WithoutPlatformMigration(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PlatformMigrationStart))
                .ToList())
            {
                services.Remove(descriptor);
            }
        });
}
