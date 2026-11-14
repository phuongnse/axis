using System.Net;
using System.Net.Sockets;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.PackageHost;
using Axis.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// With <c>Extensions:ApplicationPackages</c> on, the worker asks the real package host, <see cref="TestPackageHost"/>, whether it runs
/// under gVisor before it reads a tenant, see docs/reference/extension-packages.md#an-applications-packages. The host's answer comes from
/// the kernel log that each test feeds it.
/// </summary>
public sealed class ApplicationSandboxStartTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string PlainLinux = "<5>[    0.000000] Linux version 6.8.0 (build@host)\n<6>[    0.000001] Starting gVisor...\n";
    private const string Gvisor = "<6>[    0.000000] Starting gVisor...\n<6>[    0.100000] Waiting for children...\n";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Worker_process_exits_with_code_one_and_names_the_host_and_gvisor_when_the_host_does_not_run_under_gvisor()
    {
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path, new Sandbox(() => PlainLinux));

        // The check runs before any database work, so the tenant's database is not there.
        var (exitCode, text) = await WorkerProcess.RunAsync(
            new Dictionary<string, string>
            {
                ["DOTNET_ENVIRONMENT"] = "Testing",
                ["Tenants__a__Hosts__0"] = "a.example.test",
                ["Tenants__a__ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
                ["Extensions__Path"] = extensions.Path,
                ["Extensions__ApplicationPackages"] = "true",
                ["Extensions__Runners__Application"] = host.Address,

                // Any free port, so a worker that runs under scripts/dev.sh does not hold the address.
                ["Health__Urls"] = "http://127.0.0.1:0",
            },
            stopWhenStarted: false,
            CancellationToken);

        Assert.True(exitCode == 1, $"Exit code {exitCode}. Output:{Environment.NewLine}{text}");
        Assert.True(text.Contains("application-package-host", StringComparison.Ordinal), text);
        Assert.True(text.Contains("gVisor", StringComparison.Ordinal), text);
        Assert.Equal([false], host.SandboxAnswers());
    }

    [Fact]
    public async Task Worker_becomes_ready_when_the_host_runs_under_gvisor()
    {
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path, new Sandbox(() => Gvisor));

        await AssertReadyAsync(host, applicationPackages: "true");

        Assert.Equal([true], host.SandboxAnswers());
    }

    [Fact]
    public async Task Worker_becomes_ready_without_asking_the_host_when_application_packages_are_off()
    {
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path, new Sandbox(() => PlainLinux));

        await AssertReadyAsync(host, applicationPackages: "false");

        Assert.Empty(host.SandboxAnswers());
    }

    [Fact]
    public async Task Check_fails_with_a_no_answer_diagnostic_when_the_host_accepts_the_connection_and_never_answers()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepted = listener.AcceptTcpClientAsync(CancellationToken).AsTask();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var check = new ApplicationSandboxCheck(new ApplicationPackageSettings(true, "127.0.0.1", port), NullLogger<ApplicationSandboxCheck>.Instance);

        var failure = await check.RunAsync(TimeSpan.FromMilliseconds(200), CancellationToken);

        using var client = await accepted;
        Assert.NotNull(failure);
        Assert.Contains("application-package-host", failure, StringComparison.Ordinal);
        Assert.Contains($"127.0.0.1:{port}", failure, StringComparison.Ordinal);
        Assert.Contains("did not answer", failure, StringComparison.Ordinal);
        Assert.Contains("gVisor", failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_fails_when_nothing_listens_at_the_host_address()
    {
        var (host, port) = (IPAddress.Loopback.ToString(), ClosedPort());
        var check = new ApplicationSandboxCheck(new ApplicationPackageSettings(true, host, port), NullLogger<ApplicationSandboxCheck>.Instance);

        var failure = await check.RunAsync(TimeSpan.FromSeconds(30), CancellationToken);

        Assert.NotNull(failure);
        Assert.Contains("application-package-host", failure, StringComparison.Ordinal);
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Starts the worker in this process on a migrated database and waits until <c>/health/ready</c> is healthy.</summary>
    private async Task AssertReadyAsync(TestPackageHost host, string applicationPackages)
    {
        var connectionString = await database.CreateDatabaseAsync("application_sandbox", CancellationToken);
        await WorkerHealthTests.MigrateAsync(connectionString);
        await using var app = WorkerHealthTests.BuildWorker(
            new LogCollector(),
            connectionString,
            handler: null,
            ("Extensions:ApplicationPackages", applicationPackages),
            ("Extensions:Runners:Application", host.Address));
        await app.StartAsync(CancellationToken);
        using var client = WorkerHealthTests.ClientOf(app);

        await WaitUntilAsync(
            async () => await WorkerHealthTests.GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Healthy"),
            "The worker did not become ready.");
    }
}
