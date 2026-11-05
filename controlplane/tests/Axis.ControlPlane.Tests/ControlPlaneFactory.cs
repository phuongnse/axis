using Axis.ControlPlane.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.ControlPlane.Tests;

/// <summary>Starts the control plane in a test host with no database migration, so a test needs no PostgreSQL.</summary>
internal static class ControlPlaneFactory
{
    /// <summary>A connection to a port nothing listens on, so the database is unreachable and fails fast.</summary>
    public const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=axis_controlplane;Username=u;Password=secret-password;Timeout=2";

    /// <summary>
    /// A factory in <paramref name="environment"/> with valid settings, except where <paramref name="settings"/> sets a
    /// key, and without the hosted service that applies the migrations.
    /// </summary>
    public static WebApplicationFactory<Program> Create(
        string environment = "Testing",
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            var all = new Dictionary<string, string?>
            {
                ["ControlPlane:Region"] = "vn",
                ["ControlPlane:TenantDomain"] = "vn.example.com",
                ["ControlPlane:ConnectionString"] = UnreachableConnectionString,
                ["ControlPlane:MailFrom"] = "no-reply@vn.example.com",
                ["ControlPlane:Mail:Host"] = "127.0.0.1",
                ["ControlPlane:Mail:Port"] = "1",
            };
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                all[key] = value;
            }

            foreach (var (key, value) in all)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services
                    .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ControlPlaneMigrationStart))
                    .ToList())
                {
                    services.Remove(descriptor);
                }
            });
        });
}
