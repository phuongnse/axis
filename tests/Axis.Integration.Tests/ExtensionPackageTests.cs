using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Extensions;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

public sealed class ExtensionPackageTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string TenantHost = "a.example.test";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Server_and_worker_list_the_installed_package_with_its_typed_operations()
    {
        using var root = new TemporaryFolder().Install(1);
        var connectionString = await database.CreateDatabaseAsync("packages", CancellationToken);

        await using var server = CreateServer(connectionString, root.Path, new LogCollector());
        using var worker = BuildWorker(connectionString, root.Path);

        foreach (var catalog in new[] { server.Services.GetRequiredService<PackageCatalog>(), worker.Services.GetRequiredService<PackageCatalog>() })
        {
            Assert.Empty(catalog.Diagnostics);
            var package = Assert.Single(catalog.Packages);
            Assert.Equal(("test-package", "1.0.0"), (package.Manifest.Id, package.Manifest.Version));
            Assert.Equal(3, package.Manifest.Operations.Count);

            var describe = package.Manifest.Operations[0];
            Assert.Equal(("describe", "TestPackage.DescribeOperation", true, TimeSpan.FromSeconds(30)), (describe.Name, describe.Type, describe.Idempotent, describe.Timeout));
            Assert.Equal([new PackageField("name", FieldType.Text, true)], describe.Input);
            Assert.Equal([new PackageField("greeting", FieldType.Text, true), new PackageField("version", FieldType.Text, true)], describe.Output);
            Assert.Equal(["TestUrl"], describe.Capabilities.Urls);
            Assert.Equal(["TestToken"], describe.Capabilities.Secrets);

            var sum = package.Manifest.Operations[1];
            Assert.Equal(("sum", "TestPackage.SumOperation", false, TimeSpan.FromSeconds(5)), (sum.Name, sum.Type, sum.Idempotent, sum.Timeout));
            Assert.Equal([new PackageField("a", FieldType.Integer, true), new PackageField("b", FieldType.Integer, true)], sum.Input);
            Assert.Equal([new PackageField("total", FieldType.Integer, true)], sum.Output);
            Assert.Empty(sum.Capabilities.Urls);
            Assert.Empty(sum.Capabilities.Secrets);

            var fail = package.Manifest.Operations[2];
            Assert.Equal(("fail", "TestPackage.FailOperation", false, TimeSpan.FromSeconds(30)), (fail.Name, fail.Type, fail.Idempotent, fail.Timeout));
            Assert.Equal([new PackageField("a", FieldType.Integer, true)], fail.Input);
            Assert.Empty(fail.Output);
        }
    }

    [Fact]
    public async Task Two_versions_of_a_package_load_side_by_side_and_each_runs_its_own_code()
    {
        using var root = new TemporaryFolder().Install(1, 2);
        using var worker = BuildWorker(await database.CreateDatabaseAsync("packages", CancellationToken), root.Path);
        var catalog = worker.Services.GetRequiredService<PackageCatalog>();

        Assert.Equal(["1.0.0", "2.0.0"], catalog.Packages.Select(package => package.Manifest.Version));
        var first = catalog.Find(TestPackages.Id, "1.0.0")!;
        var second = catalog.Find(TestPackages.Id, "2.0.0")!;
        Assert.NotSame(first.LoadContext, second.LoadContext);

        Dictionary<string, object?> input = new() { ["name"] = "Anna" };
        var fromFirst = await first.CreateOperation("describe").RunAsync(input, new StubContext(), CancellationToken);
        var fromSecond = await second.CreateOperation("describe").RunAsync(input, new StubContext(), CancellationToken);

        Assert.Equal(("Hello, Anna.", "1.0.0"), (fromFirst["greeting"], fromFirst["version"]));
        Assert.Equal(("Hello, Anna.", "2.0.0"), (fromSecond["greeting"], fromSecond["version"]));
        var total = await second.CreateOperation("sum").RunAsync(new Dictionary<string, object?> { ["a"] = 2L, ["b"] = 3L }, new StubContext(), CancellationToken);
        Assert.Equal(5L, total["total"]);
    }

    [Fact]
    public async Task Server_starts_without_a_broken_package_and_logs_its_diagnostic_as_a_warning()
    {
        using var root = new TemporaryFolder().Install(1, 2)
            .EditManifest("2.0.0", manifest => manifest["operations"]![0]!["type"] = "TestPackage.Missing");
        var logs = new LogCollector();

        await using var server = CreateServer(await database.CreateDatabaseAsync("packages", CancellationToken), root.Path, logs);
        server.CreateClient().Dispose();

        var package = Assert.Single(server.Services.GetRequiredService<PackageCatalog>().Packages);
        Assert.Equal("1.0.0", package.Manifest.Version);
        Assert.Contains(logs.Entries, entry =>
            entry.StartsWith("Warning: ", StringComparison.Ordinal)
            && entry.Contains(DiagnosticCodes.InvalidPackageOperationType, StringComparison.Ordinal)
            && entry.Contains("test-package/2.0.0/axis-package.json /operations/0/type", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_setting_a_host_has_no_packages_and_a_missing_folder_fails_its_start()
    {
        const string connectionString = "Host=localhost;Database=unused";
        using (var worker = BuildWorker(connectionString, packagePath: null))
        {
            Assert.Empty(worker.Services.GetRequiredService<PackageCatalog>().Packages);
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildWorker(connectionString, Path.Combine(Path.GetTempPath(), $"axis-missing-{Guid.NewGuid():N}")).Dispose());
        Assert.Contains(PackageCatalog.PathSetting, exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateServer(string connectionString, string packagePath, LogCollector logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", TenantHost);
            builder.UseSetting("Tenants:a:ConnectionString", connectionString);
            builder.UseSetting(PackageCatalog.PathSetting, packagePath);
            builder.UseTestOidc("a");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    private static IHost BuildWorker(string connectionString, string? packagePath)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = TenantHost,
            ["Tenants:a:ConnectionString"] = connectionString,
            [PackageCatalog.PathSetting] = packagePath,
        });
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        return builder.Build();
    }

    private sealed class StubContext : IOperationContext
    {
        public Guid OperationId { get; } = Guid.NewGuid();

        public HttpClient GetHttpClient(string urlBinding) => throw new InvalidOperationException($"'{urlBinding}' is not declared.");

        public string GetSecret(string secretBinding) => throw new InvalidOperationException($"'{secretBinding}' is not declared.");
    }
}
