using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Startup activation of the purchase request sample on a main tenant, <c>a</c>, which has its deployment bindings,
/// and on its preview tenant, <c>a-preview</c>, which has none. The preview tenant's missing bindings are warnings, so the
/// server starts and the sample is active in both databases. The preview tenant never uses the main tenant's bindings.
/// </summary>
public sealed class PreviewTenantActivationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_preview_tenant_without_bindings_activates_the_sample_and_logs_a_warning_for_each_missing_binding()
    {
        var (main, preview) = (await database.CreateDatabaseAsync("preview_main", CancellationToken), await database.CreateDatabaseAsync("preview_preview", CancellationToken));
        var logs = new LogCollector();
        await using var factory = CreateFactory(main, preview, logs, previewBindings: false);

        var exception = Record.Exception(() => factory.CreateClient().Dispose());

        Assert.Null(exception);
        Assert.NotNull(await ActiveAsync(main));
        Assert.NotNull(await ActiveAsync(preview));
        Assert.Contains(logs.Entries, entry =>
            entry.StartsWith("Warning:", StringComparison.Ordinal)
            && entry.Contains(DiagnosticCodes.MissingDeploymentBinding, StringComparison.Ordinal)
            && entry.Contains("tenant a-preview", StringComparison.Ordinal)
            && entry.Contains("PurchasingSystemUrl", StringComparison.Ordinal));
        foreach (var name in new[] { CompanyBindings.Issuer, CompanyBindings.ClientId, CompanyBindings.ClientSecret })
        {
            Assert.Contains(logs.Entries, entry =>
                entry.StartsWith("Warning:", StringComparison.Ordinal)
                && entry.Contains(DiagnosticCodes.MissingDeploymentBinding, StringComparison.Ordinal)
                && entry.Contains("tenant a-preview", StringComparison.Ordinal)
                && entry.Contains(name, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.MissingDeploymentBinding, StringComparison.Ordinal)
            && entry.Contains("for tenant a ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_preview_tenant_with_a_binding_of_the_wrong_kind_stops_the_start()
    {
        var (main, preview) = (await database.CreateDatabaseAsync("preview_main", CancellationToken), await database.CreateDatabaseAsync("preview_preview", CancellationToken));
        var logs = new LogCollector();
        await using var factory = CreateFactory(main, preview, logs, previewBindings: true);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("a-preview", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(logs.Entries, entry =>
            entry.StartsWith("Error:", StringComparison.Ordinal)
            && entry.Contains(DiagnosticCodes.MissingDeploymentBinding, StringComparison.Ordinal)
            && entry.Contains("tenant a-preview", StringComparison.Ordinal)
            && entry.Contains("PurchasingSystemUrl", StringComparison.Ordinal));
        Assert.Null(await ActiveAsync(preview));
    }

    // With previewBindings, the preview tenant's PurchasingSystemUrl is a secret, which is the wrong kind for a url.
    private static WebApplicationFactory<Program> CreateFactory(string main, string preview, LogCollector logs, bool previewBindings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(main);
            builder.UseSetting("Tenants:a:Hosts:0", "a.example.test");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(main));
            builder.UseSetting("Tenants:a:SchemaConnectionString", main);
            builder.UseSetting("Tenants:a:Bindings:PurchasingSystemUrl:Kind", "url");
            builder.UseSetting("Tenants:a:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
            foreach (var (key, value) in PurchasingBindings.Settings("a"))
            {
                builder.UseSetting(key, value);
            }
            foreach (var (key, value) in CompanyBindings.Settings("a"))
            {
                builder.UseSetting(key, value);
            }
            builder.UseSetting("Tenants:a-preview:Hosts:0", "preview.example.test");
            builder.UseSetting("Tenants:a-preview:ConnectionString", PostgreSqlServer.RuntimeConnectionString(preview));
            builder.UseSetting("Tenants:a-preview:SchemaConnectionString", preview);
            builder.UseSetting("Tenants:a-preview:PreviewOf", "a");
            if (previewBindings)
            {
                builder.UseSetting("Tenants:a-preview:Bindings:PurchasingSystemUrl:Kind", "secret");
                builder.UseSetting("Tenants:a-preview:Bindings:PurchasingSystemUrl:Value", "not-a-url");
            }

            builder.UseTestOidc("a");
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("ActivateOnStartup:0", _appFolder);
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    private static async Task<ActiveRelease?> ActiveAsync(string connectionString)
    {
        await using var context = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        return await new ActiveReleaseStore(context).FindByNameAsync("PurchaseRequests", CancellationToken);
    }
}
