using System.Net;
using Axis.Server.Tests;
using Axis.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.StudioPublishTests;

namespace Axis.Integration.Tests;

/// <summary>
/// The package check of <c>publish</c> and <c>preview</c>, see <c>docs/reference/tenant-quotas.md#refusals</c>. The
/// <c>author</c> user holds the author and publisher roles and not the developer role, so a publish of a folder with a
/// package needs the publisher role only. The host is a cell whose <c>packageCpuSecondsPerDay</c> is set per test, see
/// <see cref="StorageQuotaHost"/>. The platform tables live in tenant <c>a</c>'s database here, where the server reads them.
/// </summary>
public sealed class StudioPackageQuotaTests : IAsyncLifetime
{
    private const string PublishUrl = "/api/studio/apps/PurchaseRequests/publish";
    private const string PreviewUrl = "/api/studio/apps/PurchaseRequests/preview";
    private const string Title = "This tenant has reached its package run time quota.";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task With_package_run_time_a_publisher_without_the_developer_role_publishes_an_application_with_a_package()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_package_quota_allowed", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)])
            .WithWebHostBuilder(builder => builder.InCell(3600, QuotaKeys.PackageCpuSecondsPerDay));
        using var client = factory.CreateClient().SignedInAs("author");
        await AddPackageAsync(folder.Path);

        using var publish = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);

        Assert.Equal(HttpStatusCode.Accepted, publish.StatusCode);
        Assert.Equal(1, await CountRequestsAsync(connectionString));
    }

    [Fact]
    public async Task Without_package_run_time_a_publish_and_a_preview_with_a_package_get_the_quota_403_and_store_nothing()
    {
        using var folder = NewFolder();
        var main = await _database.CreateDatabaseAsync("studio_package_quota_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("studio_package_quota_preview", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, main)], preview)
            .WithWebHostBuilder(builder => builder.InCell(0, QuotaKeys.PackageCpuSecondsPerDay));
        using var client = factory.CreateClient().SignedInAs("author");

        // The platform tables, which the quota's used value reads, are created in the database of tenant a.
        await StorageQuotaHost.SeedUsageAsync(main, "a", 0, 0, CancellationToken);
        await AddPackageAsync(folder.Path);

        // The 403 comes before the body, so a bad body at quota 0 still gets it.
        using var publish = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA, "not json");
        await AssertQuotaRefusalAsync(publish);
        Assert.Equal(0, await CountRequestsAsync(main));

        using var previewed = await SendAsync(client, HttpMethod.Post, PreviewUrl, HostA);
        await AssertQuotaRefusalAsync(previewed);
        Assert.Equal(0, await CountRequestsAsync(main));

        Directory.Delete(Path.Combine(folder.Path, "packages"), recursive: true);
        using var withoutPackage = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);
        Assert.Equal(HttpStatusCode.Accepted, withoutPackage.StatusCode);
        Assert.Equal(1, await CountRequestsAsync(main));
    }

    [Fact]
    public async Task Outside_a_cell_a_publish_with_a_package_is_not_refused()
    {
        using var folder = NewFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_package_quota_outside", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var client = factory.CreateClient().SignedInAs("author");
        await AddPackageAsync(folder.Path);

        using var publish = await SendAsync(client, HttpMethod.Post, PublishUrl, HostA);

        Assert.Equal(HttpStatusCode.Accepted, publish.StatusCode);
        Assert.Equal(1, await CountRequestsAsync(connectionString));
    }

    // Startup activation compiles the folder, and a package fails the start while Extensions:ApplicationPackages is off,
    // so the package is written after the server has started.
    private static async Task AddPackageAsync(string folder)
    {
        var package = Path.Combine(folder, "packages", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(package);
        await File.WriteAllTextAsync(Path.Combine(package, "axis-package.json"), "{}", CancellationToken);
    }

    private static async Task AssertQuotaRefusalAsync(HttpResponseMessage response)
    {
        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        var root = problem.RootElement;
        Assert.Equal(Title, root.GetProperty("title").GetString());
        Assert.Equal("quota-exceeded", root.GetProperty("reason").GetString());
        Assert.Equal(QuotaKeys.PackageCpuSecondsPerDay, root.GetProperty("quota").GetString());
        Assert.Equal(0, root.GetProperty("limit").GetInt64());
        Assert.Equal(0, root.GetProperty("used").GetInt64());
    }
}
