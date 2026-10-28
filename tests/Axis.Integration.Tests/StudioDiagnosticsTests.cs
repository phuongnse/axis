using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Axis.Integration.Tests;

/// <summary>
/// The Studio diagnostics route compiles a folder in memory. Against a real tenant database, it stores
/// no release and leaves the active release as it was, even after the folder changed.
/// </summary>
public sealed class StudioDiagnosticsTests : IAsyncLifetime
{
    private const string Tenant = "a";

    private const string Host = "a.example.test";

    private const string DiagnosticsUrl = "/api/studio/apps/PurchaseRequests/diagnostics";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Diagnostics_leave_the_stored_releases_and_the_active_release_unchanged()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_diagnostics", TestContext.Current.CancellationToken);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", connectionString);
            builder.UseTestOidc(Tenant);
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Kind", "url");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Kind", "secret");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Value", "studio-test-token");
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("ActivateOnStartup:0", folder.Path);
            builder.UseSetting("Studio:Folders:0", folder.Path);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"));
        });

        // Creating a client starts the server, which migrates the database and activates the folder first.
        using var client = factory.CreateClient().SignedInAs("author");
        var releasesBefore = await CountReleasesAsync(connectionString);
        var activeBefore = await ActiveReleaseIdAsync(connectionString);
        Assert.Equal(1, releasesBefore);

        var textsPath = Path.Combine(folder.Path, "texts", "en.json");
        var texts = await File.ReadAllTextAsync(textsPath, CancellationToken);
        await File.WriteAllTextAsync(textsPath, texts.Replace("\"Departments\"", "\"Teams\"", StringComparison.Ordinal), CancellationToken);
        Assert.Empty(await ErrorsAsync(client));

        var entityPath = Path.Combine(folder.Path, "entities", "purchase-request.json");
        var entity = await File.ReadAllTextAsync(entityPath, CancellationToken);
        await File.WriteAllTextAsync(entityPath, entity.Replace("\"target\": \"Department\"", "\"target\": \"NoSuchEntity\"", StringComparison.Ordinal), CancellationToken);
        Assert.Contains("AXC0012", await ErrorsAsync(client));

        Assert.Equal(releasesBefore, await CountReleasesAsync(connectionString));
        Assert.Equal(activeBefore, await ActiveReleaseIdAsync(connectionString));
    }

    private static async Task<List<string?>> ErrorsAsync(HttpClient client)
    {
        using var request = RecordApiFixture.Request(DiagnosticsUrl, Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Where(item => item.GetProperty("severity").GetString() == "error")
            .Select(item => item.GetProperty("code").GetString())];
    }

    private static async Task<long> CountReleasesAsync(string connectionString) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, "SELECT count(*) FROM axis.releases", CancellationToken))!;

    private static async Task<Guid> ActiveReleaseIdAsync(string connectionString)
    {
        await using var configuration = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        var active = await new ActiveReleaseStore(configuration).FindByNameAsync("PurchaseRequests", CancellationToken);
        Assert.NotNull(active);
        return active.ReleaseId;
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
