using Axis.Configuration.Tests;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Three tenants, <c>a</c>, <c>b</c> and <c>c</c>, each with its own database holding StepApp plus a
/// schedule trigger. <c>ReviewLargeRequests</c> runs at 06:00 Berlin time and starts
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
    private IConfiguration? _settings;

    /// <summary>Every log entry of the workers.</summary>
    internal LogCollector Logs { get; } = new();

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

        foreach (var id in new[] { "a", "b", "c" })
        {
            var tenant = new ProcessStepFixture(id, _folder.Path);
            _tenantList.Add(tenant);
            _tenants[id] = tenant;
            await tenant.InitializeAsync();
        }

        var values = new Dictionary<string, string?>();
        foreach (var (tenantId, tenant) in _tenants)
        {
            values[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            values[$"Tenants:{tenantId}:ConnectionString"] = tenant.ConnectionString;
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
    }

    /// <summary>The database of <paramref name="tenantId"/>.</summary>
    public ProcessStepFixture Tenant(string tenantId) => _tenants[tenantId];

    /// <summary>Builds a worker for the three tenants. The fixture disposes it.</summary>
    public WorkItemRunner NewWorker()
    {
        var worker = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(_settings ?? throw new InvalidOperationException("The fixture is not initialized."), new HostingEnvironment { EnvironmentName = "Testing" })
            .BuildServiceProvider();
        _workers.Add(worker);
        return worker.GetRequiredService<WorkItemRunner>();
    }

    /// <summary>Claims and runs the tenant's due work items of every kind on <paramref name="worker"/> until none is left.</summary>
    public static async Task RunUntilIdleAsync(string tenantId, WorkItemRunner worker, CancellationToken cancellationToken)
    {
        while (await worker.RunNextAsync(tenantId, _lease, cancellationToken))
        {
        }
    }
}
