using System.Diagnostics;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class PinnedReleaseStartTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string TenantHost = "a.example.test";

    // Each test uses its own application and database, so releases never meet across tests.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly string _name = $"Pinned{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Server_start_stops_with_the_diagnostic_when_the_active_release_pins_a_version_that_is_not_installed()
    {
        using var both = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var newer = new TemporaryFolder().Install("1.1.0");
        using var folder = ApplicationFolder("1.0.0");
        var (connectionString, first) = await ActivateFirstReleaseAsync(folder, both.Path);

        var logs = new LogCollector();
        await using var server = CreateServer(connectionString, folder: null, newer.Path, logs);

        Assert.ThrowsAny<Exception>(() => server.CreateClient());

        AssertDiagnostic(logs, first);
    }

    [Fact]
    public async Task Server_start_stops_with_the_diagnostic_when_a_release_that_is_not_active_has_a_waiting_instance()
    {
        using var both = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var newer = new TemporaryFolder().Install("1.1.0");
        using var folder = ApplicationFolder("1.0.0");
        var (connectionString, first) = await ActivateFirstReleaseAsync(folder, both.Path);
        var second = await ActivateSecondReleaseAsync(folder, connectionString, both.Path, first);
        await InsertInstanceAsync(connectionString, first, "waiting");

        var logs = new LogCollector();
        await using var server = CreateServer(connectionString, folder: null, newer.Path, logs);

        Assert.ThrowsAny<Exception>(() => server.CreateClient());

        AssertDiagnostic(logs, first);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(second.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_starts_when_an_older_release_with_only_ended_instances_pins_a_removed_version()
    {
        using var both = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var newer = new TemporaryFolder().Install("1.1.0");
        using var folder = ApplicationFolder("1.0.0");
        var (connectionString, first) = await ActivateFirstReleaseAsync(folder, both.Path);
        await ActivateSecondReleaseAsync(folder, connectionString, both.Path, first);
        await InsertInstanceAsync(connectionString, first, "completed");

        var logs = new LogCollector();
        await using var server = CreateServer(connectionString, folder: null, newer.Path, logs);

        server.CreateClient().Dispose();

        Assert.DoesNotContain(logs.Entries, entry => entry.Contains(DiagnosticCodes.PinnedPackageVersionMissing, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Worker_host_stops_with_the_diagnostic_and_claims_no_work_item_when_the_active_release_pins_a_version_that_is_not_installed()
    {
        using var both = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var newer = new TemporaryFolder().Install("1.1.0");
        using var folder = ApplicationFolder("1.0.0");
        var (connectionString, first) = await ActivateFirstReleaseAsync(folder, both.Path);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await WorkItemQueue.EnqueueAsync(
                connection, null, "a", ProcessStarts.StepWorkItemKind, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
        }

        var logs = new LogCollector();
        using var worker = BuildWorker(connectionString, newer.Path, logs);
        var stopping = new TaskCompletionSource();
        using var registration = worker.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        await worker.StartAsync(CancellationToken);
        Assert.Same(stopping.Task, await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken)));
        await worker.StopAsync(CancellationToken);

        AssertDiagnostic(logs, first);
        Assert.Equal(
            1L,
            await ScalarAsync(connectionString, "SELECT count(*) FROM axis.process_work_items WHERE tenant_id = 'a' AND claim_token IS NULL"));
    }

    [Fact]
    public async Task Worker_process_exits_with_a_non_zero_code_and_prints_the_diagnostic_when_the_active_release_pins_a_version_that_is_not_installed()
    {
        using var both = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var newer = new TemporaryFolder().Install("1.1.0");
        using var folder = ApplicationFolder("1.0.0");
        var (connectionString, _) = await ActivateFirstReleaseAsync(folder, both.Path);

        var (exitCode, text) = await RunWorkerAsync(connectionString, newer.Path, stopWhenStarted: false);

        Assert.True(exitCode == 1, $"Exit code {exitCode}. Output:{Environment.NewLine}{text}");
        Assert.True(text.Contains(DiagnosticCodes.PinnedPackageVersionMissing, StringComparison.Ordinal), text);
    }

    [Fact]
    public async Task Worker_process_exits_with_code_zero_after_a_normal_shutdown_when_every_pinned_version_is_installed()
    {
        using var both = new TemporaryFolder().Install("1.0.0", "1.1.0");
        using var folder = ApplicationFolder("1.0.0");
        var (connectionString, _) = await ActivateFirstReleaseAsync(folder, both.Path);

        var (exitCode, text) = await RunWorkerAsync(connectionString, both.Path, stopWhenStarted: true);

        Assert.True(exitCode == 0, $"Exit code {exitCode}. Output:{Environment.NewLine}{text}");
        Assert.DoesNotContain(DiagnosticCodes.PinnedPackageVersionMissing, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the built worker as its own process. With <paramref name="stopWhenStarted"/> it sends
    /// SIGTERM once the host reports it started, for a normal shutdown. Otherwise it waits for the
    /// worker to exit on its own.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunWorkerAsync(string connectionString, string packages, bool stopWhenStarted)
    {
        var directory = WorkerDirectory();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", "Axis.Worker.dll")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["Tenants__a__Hosts__0"] = TenantHost,
            ["Tenants__a__ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
            ["Extensions__Path"] = packages,
            ["Worker__PollInterval"] = "00:00:00.05",

            // Any free port, so a worker that runs under scripts/dev.sh does not hold the address.
            ["Health__Urls"] = "http://127.0.0.1:0",
        })
        {
            process.StartInfo.Environment[key] = value;
        }

        var output = new System.Text.StringBuilder();
        var started = new TaskCompletionSource();
        void Receive(string? line)
        {
            lock (output)
            {
                output.AppendLine(line);
            }

            if (line is not null && line.Contains("Application started", StringComparison.Ordinal))
            {
                started.TrySetResult();
            }
        }

        process.OutputDataReceived += (_, e) => Receive(e.Data);
        process.ErrorDataReceived += (_, e) => Receive(e.Data);
        Assert.True(process.Start());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            if (stopWhenStarted)
            {
                await started.Task.WaitAsync(timeout.Token);
                // Give the start check time to run before the shutdown.
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                using var signal = Process.Start("kill", $"-TERM {process.Id}")!;
                await signal.WaitForExitAsync(timeout.Token);
            }

            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!CancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            string partial;
            lock (output)
            {
                partial = output.ToString();
            }

            Assert.Fail($"The worker did not finish within 60 seconds. Output:{Environment.NewLine}{partial}");
        }

        process.WaitForExit();
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    /// <summary>
    /// The folder with the built worker. The worker project's own build output is complete. The
    /// test output holds a copy that lacks the shared ASP.NET Core assemblies the test host
    /// brings, so it is used only when the project output cannot be found.
    /// </summary>
    private static string WorkerDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Axis.slnx")))
        {
            repository = repository.Parent;
        }

        if (repository is not null)
        {
            var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var own = Path.Combine(repository.FullName, "src", "Axis.Worker", "bin", framework.Parent!.Name, framework.Name);
            if (File.Exists(Path.Combine(own, "Axis.Worker.runtimeconfig.json")))
            {
                return own;
            }
        }

        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "Axis.Worker.runtimeconfig.json")),
            "The built worker was not found in the worker project's output or the test output.");
        return AppContext.BaseDirectory;
    }

    /// <summary>Activates a release pinning <c>1.0.0</c> in a new tenant database and returns its id.</summary>
    private async Task<(string ConnectionString, Guid ReleaseId)> ActivateFirstReleaseAsync(TemporaryFolder folder, string packages)
    {
        var connectionString = await database.CreateDatabaseAsync("pinned", CancellationToken);
        await using (var server = CreateServer(connectionString, folder.Path, packages, new LogCollector()))
        {
            server.CreateClient().Dispose();
        }

        var releaseId = await ActiveReleaseAsync(connectionString);
        return (connectionString, releaseId);
    }

    /// <summary>Changes the folder to pin <c>1.1.0</c> and activates it, so another release is active.</summary>
    private async Task<Guid> ActivateSecondReleaseAsync(TemporaryFolder folder, string connectionString, string packages, Guid first)
    {
        folder.With("application.json", ManifestFile("1.1.0"));
        await using (var server = CreateServer(connectionString, folder.Path, packages, new LogCollector()))
        {
            server.CreateClient().Dispose();
        }

        var second = await ActiveReleaseAsync(connectionString);
        Assert.NotEqual(first, second);
        return second;
    }

    private void AssertDiagnostic(LogCollector logs, Guid releaseId) =>
        Assert.Contains(logs.Entries, entry =>
            entry.Contains(DiagnosticCodes.PinnedPackageVersionMissing, StringComparison.Ordinal)
            && entry.Contains("'a'", StringComparison.Ordinal)
            && entry.Contains($"'{_name}'", StringComparison.Ordinal)
            && entry.Contains($"'{releaseId}'", StringComparison.Ordinal)
            && entry.Contains($"'{TestPackages.Id}' version '1.0.0'", StringComparison.Ordinal)
            && entry.Contains("application.json /packages/0/version", StringComparison.Ordinal));

    private async Task<Guid> ActiveReleaseAsync(string connectionString) =>
        (Guid)(await ScalarAsync(connectionString, $"SELECT release_id FROM axis.active_releases WHERE application_id = '{_applicationId}'"))!;

    private async Task InsertInstanceAsync(string connectionString, Guid releaseId, string state) =>
        await ScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, ended_at)
            VALUES ('{Guid.NewGuid()}', '{_applicationId}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{Guid.NewGuid()}', '{releaseId}', '{state}', 1, 'start',
                {(state is ProcessStarts.Running or ProcessStarts.Waiting ? "NULL" : "now()")})
            """);

    private TemporaryFolder ApplicationFolder(string version) =>
        new TemporaryFolder().With("application.json", ManifestFile(version));

    private string ManifestFile(string version) =>
        $$"""
        { "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1,
          "packages": [{ "id": "{{TestPackages.Id}}", "version": "{{version}}" }] }
        """;

    private static WebApplicationFactory<Program> CreateServer(string connectionString, string? folder, string packages, LogCollector logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting("Tenants:a:Hosts:0", TenantHost);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", connectionString);
            builder.UseSetting(PackageCatalog.PathSetting, packages);
            builder.UseTestOidc("a");
            if (folder is not null)
            {
                builder.UseSetting("ActivateOnStartup:0", folder);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    private static IHost BuildWorker(string connectionString, string packages, LogCollector logs)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = TenantHost,
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
            [PackageCatalog.PathSetting] = packages,
            ["Worker:PollInterval"] = "00:00:00.05",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        return builder.Build();
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(CancellationToken);
    }
}
