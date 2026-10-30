using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenants, <c>a</c> and <c>b</c>, each with its own database, and a <see cref="ConnectorStub"/> that
/// stands in for the purchasing system. Tenant <c>a</c> runs ModuleSlotAppA and tenant <c>b</c> runs
/// ModuleSlotAppB, and both install the <c>ordering</c> test module under <c>Fixtures/Modules</c>. Each
/// application maps the module's <c>purchasing</c> slot to its own connector. Each tenant's
/// <c>PurchasingUrl</c> binding points at its own path on the stub, <c>/tenant-a</c> or <c>/tenant-b</c>,
/// and its <c>PurchasingToken</c> secret holds its own token. One worker runs in Development for both
/// tenants. The host is never started, so a test runs the work items itself.
/// </summary>
public sealed class ModuleConnectorCallFixture : IAsyncLifetime
{
    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, ProcessStepFixture> _tenants = new(StringComparer.Ordinal)
    {
        ["a"] = new ProcessStepFixture("a", Path.Combine(_fixtures, "ModuleSlotAppA"), Path.Combine(_fixtures, "Modules")),
        ["b"] = new ProcessStepFixture("b", Path.Combine(_fixtures, "ModuleSlotAppB"), Path.Combine(_fixtures, "Modules")),
    };

    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private ConnectorStub? _stub;
    private ServiceProvider? _development;

    public ConnectorStub Stub => _stub ?? throw new InvalidOperationException("The fixture is not initialized.");

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
            values[$"Tenants:{tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant.ConnectionString);
            values[$"Tenants:{tenantId}:Bindings:PurchasingUrl:Kind"] = "url";
            values[$"Tenants:{tenantId}:Bindings:PurchasingUrl:Value"] = $"{_stub.BaseUrl}/tenant-{tenantId}";
            values[$"Tenants:{tenantId}:Bindings:PurchasingToken:Kind"] = "secret";
            values[$"Tenants:{tenantId}:Bindings:PurchasingToken:Value"] = _tokens[tenantId];
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        _development = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new LogCollector()).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Development" })
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_development is not null)
        {
            await _development.DisposeAsync();
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

    /// <summary>The database of <paramref name="tenantId"/>.</summary>
    public ProcessStepFixture Tenant(string tenantId) => _tenants[tenantId];

    /// <summary>The value of the <c>PurchasingToken</c> secret of <paramref name="tenantId"/>.</summary>
    public string Token(string tenantId) => _tokens[tenantId];

    /// <summary>Runs the tenant's next due step item. Returns its outcome, or <see langword="null"/> when there is none.</summary>
    public Task<WorkItemOutcome?> RunStepAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(tenantId, ProcessStarts.StepWorkItemKind, cancellationToken);

    /// <summary>Runs the tenant's next due connector call item.</summary>
    public Task<WorkItemOutcome?> RunCallAsync(string tenantId, CancellationToken cancellationToken) =>
        RunAsync(tenantId, ProcessStarts.ConnectorCallWorkItemKind, cancellationToken);

    /// <summary>
    /// Inserts a purchase order of the module and starts <c>ordering/Place</c> for it, then runs its
    /// step, which stages the call. Returns the order and the instance.
    /// </summary>
    public async Task<(Guid OrderId, Guid InstanceId)> StageAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = Tenant(tenantId);
        var orderId = await InsertOrderAsync(tenantId);
        var instanceId = await tenant.StartOnSubjectAsync("ordering/Place", orderId, tenant.FirstReleaseId);
        Assert.Equal(WorkItemOutcome.Completed, await RunStepAsync(tenantId, cancellationToken));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);
        return (orderId, instanceId);
    }

    /// <summary>Inserts a purchase order into the tenant's copy of the module's table and returns its id.</summary>
    public async Task<Guid> InsertOrderAsync(string tenantId)
    {
        var tenant = Tenant(tenantId);
        var id = Guid.NewGuid();
        await using var command = tenant.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table(tenant)} ({Column("id")}, {Column("title")})
            VALUES (@id, @title)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Order {id:N}");
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The <c>orderNumber</c> of the purchase order <paramref name="id"/>, or <see langword="null"/> when it has none.</summary>
    public async Task<string?> OrderNumberAsync(string tenantId, Guid id)
    {
        var tenant = Tenant(tenantId);
        return (string?)await tenant.ScalarAsync(
            $"SELECT {Column("orderNumber")} FROM {Table(tenant)} WHERE {Column("id")} = @id",
            ("id", id));
    }

    private static string Table(ProcessStepFixture tenant)
    {
        Assert.True(tenant.Model.TryGetEntity("ordering/PurchaseOrder", out var model));
        return EntityNaming.QualifiedTable(EntityNaming.Table(model.Id));
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private async Task<WorkItemOutcome?> RunAsync(string tenantId, string kind, CancellationToken cancellationToken)
    {
        var runner = (_development ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = await Tenant(tenantId).ClaimAsync(kind, _lease, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }
}
