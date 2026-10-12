using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenants, <c>a</c> and <c>b</c>, each with its own StepApp database, and a
/// <see cref="ConnectorStub"/> that stands in for the purchasing system. One worker runs in
/// Development for both tenants. Each tenant's <c>PurchasingUrl</c> binding points at its own path
/// on the stub, <c>/tenant-a</c> or <c>/tenant-b</c>, and its <c>PurchasingToken</c> secret holds
/// its own token. A second worker with the same bindings runs as Production, so it refuses the
/// stub's <c>http</c> URL. The hosts are never started, so a test runs the work items itself.
/// </summary>
public sealed class ConnectorCallFixture : IAsyncLifetime
{
    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, ProcessStepFixture> _tenants = new(StringComparer.Ordinal)
    {
        ["a"] = new ProcessStepFixture("a"),
        ["b"] = new ProcessStepFixture("b"),
    };

    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private ConnectorStub? _stub;
    private ServiceProvider? _development;
    private ServiceProvider? _production;

    public ConnectorStub Stub => _stub ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>Every log entry of both workers.</summary>
    internal LogCollector Logs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        foreach (var tenant in _tenants.Values)
        {
            await tenant.InitializeAsync();
        }

        _stub = await ConnectorStub.StartAsync();
        var values = new Dictionary<string, string?>();
        foreach (var (tenantId, tenant) in _tenants)
        {
            _tokens[tenantId] = $"token-{tenantId}-{Guid.NewGuid():N}";
            values[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            values[$"Tenants:{tenantId}:ConnectionString"] = tenant.ConnectionString;
            values[$"Tenants:{tenantId}:Bindings:PurchasingUrl:Kind"] = "url";
            values[$"Tenants:{tenantId}:Bindings:PurchasingUrl:Value"] = $"{_stub.BaseUrl}/tenant-{tenantId}";
            values[$"Tenants:{tenantId}:Bindings:PurchasingToken:Kind"] = "secret";
            values[$"Tenants:{tenantId}:Bindings:PurchasingToken:Value"] = _tokens[tenantId];
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _development = Worker(settings, "Development");
        _production = Worker(settings, "Production");
    }

    public async ValueTask DisposeAsync()
    {
        if (_development is not null)
        {
            await _development.DisposeAsync();
        }

        if (_production is not null)
        {
            await _production.DisposeAsync();
        }

        if (_stub is not null)
        {
            await _stub.DisposeAsync();
        }

        foreach (var tenant in _tenants.Values)
        {
            await tenant.DisposeAsync();
        }
    }

    /// <summary>The StepApp database of <paramref name="tenantId"/>.</summary>
    public ProcessStepFixture Tenant(string tenantId) => _tenants[tenantId];

    /// <summary>The value of the <c>PurchasingToken</c> secret of <paramref name="tenantId"/>.</summary>
    public string Token(string tenantId) => _tokens[tenantId];

    /// <summary>Runs the tenant's next due step item on the Development worker. Returns its outcome, or <see langword="null"/> when there is none.</summary>
    public Task<WorkItemOutcome?> RunStepAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(_development, tenantId, ProcessStarts.StepWorkItemKind, cancellationToken);

    /// <summary>Runs the tenant's next due connector call item on the Development worker.</summary>
    public Task<WorkItemOutcome?> RunCallAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(_development, tenantId, ProcessStarts.ConnectorCallWorkItemKind, cancellationToken);

    /// <summary>Runs the tenant's next due connector call item on the Production worker.</summary>
    public Task<WorkItemOutcome?> RunCallOutsideDevelopmentAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(_production, tenantId, ProcessStarts.ConnectorCallWorkItemKind, cancellationToken);

    /// <summary>Starts the Order process for a new request of <paramref name="tenantId"/> and runs its step, which stages the call.</summary>
    public async Task<(Guid SubjectId, Guid InstanceId)> StageAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = Tenant(tenantId);
        var subjectId = await tenant.InsertRequestAsync(500m, 1);
        var instanceId = await tenant.StartAsync("Order", subjectId, tenant.FirstReleaseId);
        Assert.Equal(WorkItemOutcome.Completed, await RunStepAsync(tenantId, cancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);
        return (subjectId, instanceId);
    }

    private ServiceProvider Worker(IConfiguration settings, string environment) =>
        new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = environment })
            .BuildServiceProvider();

    private async Task<WorkItemOutcome?> RunAsync(ServiceProvider? worker, string tenantId, string kind, CancellationToken cancellationToken)
    {
        var runner = (worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = await Tenant(tenantId).ClaimAsync(kind, _lease, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }
}
