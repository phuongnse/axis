using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Eleven tenants, <c>a</c> to <c>k</c>, each with its own database holding StepApp plus a
/// schedule trigger. Tenants <c>e</c> and <c>g</c> start with <see cref="HourlyFolder"/>, a copy of
/// the application whose trigger runs every hour in UTC, and the others with the daily one. <c>ReviewLargeRequests</c> runs at 06:00 Berlin time and starts
/// <c>RoleApprove</c> for each request of the data source <c>LargeRequests</c>, those with an
/// amount of at least the fixed <c>minAmount</c> of 1000. Each test uses its own tenant, so record
/// sets do not mix. Workers are built from the worker's own registration and never started, so a
/// test runs the work items itself.
/// </summary>
public sealed class ScheduleTriggerFixture : IAsyncLifetime
{
    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly List<ProcessStepFixture> _tenantList = [];
    private readonly Dictionary<string, ProcessStepFixture> _tenants = new(StringComparer.Ordinal);
    private readonly List<ServiceProvider> _workers = [];
    private TemporaryFolder? _folder;
    private TemporaryFolder? _hourlyFolder;
    private IConfiguration? _settings;

    /// <summary>Every log entry of the workers.</summary>
    internal LogCollector Logs { get; } = new();

    /// <summary>The application whose trigger runs every day at 06:00 Berlin time, which tenants other than <c>e</c> and <c>g</c> start with.</summary>
    internal string Folder => (_folder ?? throw new InvalidOperationException("The fixture is not initialized.")).Path;

    /// <summary>A copy of the application whose trigger runs at the start of every hour, with the same ids.</summary>
    internal string HourlyFolder => (_hourlyFolder ?? throw new InvalidOperationException("The fixture is not initialized.")).Path;

    public async ValueTask InitializeAsync()
    {
        _folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(ProcessStepFixture.StepAppFolder, "*", SearchOption.AllDirectories))
        {
            _folder.With(Path.GetRelativePath(ProcessStepFixture.StepAppFolder, file), await File.ReadAllTextAsync(file));
        }

        _folder
            .With("data-sources/large-requests.json", """
                {
                  "id": "5f7cfd2e-3b6a-4c1e-9d0a-6c8f3a1b2e41",
                  "kind": "dataSource",
                  "name": "LargeRequests",
                  "formatVersion": 1,
                  "entity": "Request",
                  "fields": [{ "name": "title", "path": "title" }],
                  "parameters": [{ "name": "minAmount", "type": "decimal", "required": true }],
                  "filter": "amount >= minAmount"
                }
                """)
            .With("triggers/review-large-requests.json", """
                {
                  "id": "a1c8e0f4-7d52-4b39-8e6c-0f9b2d4a7c13",
                  "kind": "trigger",
                  "name": "ReviewLargeRequests",
                  "formatVersion": 1,
                  "type": "schedule",
                  "schedule": "0 6 * * *",
                  "timeZone": "Europe/Berlin",
                  "dataSource": "LargeRequests",
                  "parameters": { "minAmount": 1000 },
                  "process": "RoleApprove"
                }
                """);

        _hourlyFolder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_folder.Path, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_folder.Path, file);
            var content = await File.ReadAllTextAsync(file);
            // UTC keeps the hour steps even: Berlin runs a repeated hour once, so the tests would fail around the autumn clock change.
            _hourlyFolder.With(
                relativePath,
                relativePath == "triggers/review-large-requests.json"
                    ? content.Replace("\"0 6 * * *\"", "\"0 * * * *\"", StringComparison.Ordinal).Replace("\"Europe/Berlin\"", "\"UTC\"", StringComparison.Ordinal)
                    : content);
        }

        foreach (var id in new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" })
        {
            var tenant = new ProcessStepFixture(id, id is "e" or "g" ? _hourlyFolder.Path : _folder.Path);
            _tenantList.Add(tenant);
            _tenants[id] = tenant;
            await tenant.InitializeAsync();
        }

        var values = new Dictionary<string, string?>();
        foreach (var (tenantId, tenant) in _tenants)
        {
            values[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            values[$"Tenants:{tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant.ConnectionString);
        }

        _settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers)
        {
            await worker.DisposeAsync();
        }

        foreach (var tenant in _tenantList)
        {
            await tenant.DisposeAsync();
        }

        _folder?.Dispose();
        _hourlyFolder?.Dispose();
    }

    /// <summary>The database of <paramref name="tenantId"/>.</summary>
    public ProcessStepFixture Tenant(string tenantId) => _tenants[tenantId];

    /// <summary>
    /// Builds a worker for the eleven tenants. The fixture disposes it. <paramref name="deliveryAttempts"/>
    /// sets <c>Worker:DeliveryAttempts</c> and leaves the default when it is <see langword="null"/>.
    /// </summary>
    public WorkItemRunner NewWorker(int? deliveryAttempts = null) =>
        NewWorkerServices(deliveryAttempts).GetRequiredService<WorkItemRunner>();

    /// <summary>Builds the services of a worker like <see cref="NewWorker"/>, so a test can read its handlers. The fixture disposes them.</summary>
    internal ServiceProvider NewWorkerServices(int? deliveryAttempts = null)
    {
        var settings = _settings ?? throw new InvalidOperationException("The fixture is not initialized.");
        if (deliveryAttempts is { } attempts)
        {
            settings = new ConfigurationBuilder()
                .AddConfiguration(settings)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Worker:DeliveryAttempts"] = attempts.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                .Build();
        }

        var worker = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" })
            .BuildServiceProvider();
        _workers.Add(worker);
        return worker;
    }

    /// <summary>Claims and runs the tenant's due work items of every kind on <paramref name="worker"/> until none is left.</summary>
    public static async Task RunUntilIdleAsync(string tenantId, WorkItemRunner worker, CancellationToken cancellationToken)
    {
        while (await worker.RunNextAsync(tenantId, _lease, cancellationToken))
        {
        }
    }

    /// <summary>
    /// Claims and runs the tenant's next due <c>trigger.schedule</c> or <c>trigger.fanOut</c> item on
    /// <paramref name="worker"/>, <paramref name="count"/> times. Each run must find an item. The
    /// steps of the instances that a fan-out starts are left, so each run is one occurrence or one batch.
    /// </summary>
    public async Task RunAsync(string tenantId, WorkItemRunner worker, int count, CancellationToken cancellationToken)
    {
        for (var run = 0; run < count; run++)
        {
            await using var connection = await _tenants[tenantId].DataSource.OpenConnectionAsync(cancellationToken);
            var item = await WorkItemQueue.ClaimAsync(
                connection,
                tenantId,
                [ProcessStarts.TriggerScheduleWorkItemKind, ProcessStarts.TriggerFanOutWorkItemKind],
                _lease,
                cancellationToken);
            Assert.True(item is not null, $"Run {run + 1} of {count} found no due trigger work item.");
            await worker.ExecuteAsync(item, cancellationToken);
        }
    }
}
