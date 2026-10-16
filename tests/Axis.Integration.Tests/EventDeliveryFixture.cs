using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenants, <c>a</c> and <c>b</c>, each with its own StepApp database, and one worker for both
/// tenants whose log entries go to <see cref="Logs"/>. The host is never started, so a test runs the
/// work items itself, and a test can build the worker again to stand for a restart.
/// </summary>
public sealed class EventDeliveryFixture : IAsyncLifetime
{
    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, ProcessStepFixture> _tenants = new(StringComparer.Ordinal)
    {
        ["a"] = new ProcessStepFixture("a"),
        ["b"] = new ProcessStepFixture("b"),
    };

    private IConfiguration? _settings;
    private ServiceProvider? _worker;

    /// <summary>Every log entry of the worker, across restarts.</summary>
    internal LogCollector Logs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        foreach (var tenant in _tenants.Values)
        {
            await tenant.InitializeAsync();
        }

        var values = new Dictionary<string, string?>();
        foreach (var (tenantId, tenant) in _tenants)
        {
            values[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            values[$"Tenants:{tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant.ConnectionString);
        }

        _settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _worker = Worker(_settings);
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        foreach (var tenant in _tenants.Values)
        {
            await tenant.DisposeAsync();
        }
    }

    /// <summary>The StepApp database of <paramref name="tenantId"/>.</summary>
    public ProcessStepFixture Tenant(string tenantId) => _tenants[tenantId];

    /// <summary>Claims and runs the tenant's due work items of every kind on the worker until none is left.</summary>
    public async Task RunUntilIdleAsync(string tenantId, CancellationToken cancellationToken)
    {
        var runner = (_worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        while (await runner.RunNextAsync(tenantId, _lease, cancellationToken))
        {
        }
    }

    /// <summary>Disposes the worker and builds a new one with the same settings, as a restart does.</summary>
    public async Task RestartWorkerAsync()
    {
        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        _worker = Worker(_settings ?? throw new InvalidOperationException("The fixture is not initialized."));
    }

    /// <summary>Queues an <c>event.deliver</c> item for the event <paramref name="eventId"/> of the tenant, due a minute ago.</summary>
    public async Task EnqueueDeliveryAsync(string tenantId, Guid eventId)
    {
        await using var connection = await Tenant(tenantId).DataSource.OpenConnectionAsync();
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction: null,
            tenantId,
            ProcessStarts.EventDeliverWorkItemKind,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationToken.None,
            eventId: eventId);
    }

    private ServiceProvider Worker(IConfiguration settings) =>
        new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" })
            .BuildServiceProvider();
}
