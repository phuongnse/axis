using System.Security.Cryptography;
using Axis.Configuration.Model;
using Axis.Data;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// Tenant <c>a</c> with a StepApp database, a copy of StepApp that adds two webhooks on <c>RequestNoted</c>, and a
/// <see cref="WebhookReceiver"/> that stands in for the two outside systems at <c>/one</c> and <c>/two</c>. The
/// <c>HookOneSecret</c> binding holds one <c>whsec_</c> secret. The <c>HookTwoSecret</c> binding holds two secrets
/// separated by a space, one <c>whsec_</c> and one plain. A worker that runs in Development sends to the receiver's
/// <c>http</c> URLs, and a second worker that runs as Production refuses them. Both share <see cref="Clock"/>. The
/// hosts are never started, so a test runs the work items itself.
/// </summary>
public sealed class WebhookDeliveryFixture : IAsyncLifetime
{
    /// <summary>The path of the first webhook's URL at the receiver.</summary>
    public const string OnePath = "/one";

    /// <summary>The path of the second webhook's URL at the receiver.</summary>
    public const string TwoPath = "/two";

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly ProcessStepFixture _tenant = new("a");
    private WebhookReceiver? _receiver;
    private ServiceProvider? _development;
    private ServiceProvider? _production;
    private ApplicationModel? _model;

    /// <summary>The clock of the workers, which starts at the real time and moves only when a test advances it.</summary>
    internal ManualClock Clock { get; } = new();

    /// <summary>The StepApp database of the tenant.</summary>
    public ProcessStepFixture Tenant => _tenant;

    public WebhookReceiver Receiver => _receiver ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The release with the two webhooks.</summary>
    public Guid ReleaseId { get; private set; }

    /// <summary>The model of the release with the two webhooks.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The secret of the first webhook.</summary>
    public string OneSecret { get; } = Secret();

    /// <summary>The two secrets of the second webhook: a <c>whsec_</c> one and a plain one.</summary>
    public (string Prefixed, string Plain) TwoSecrets { get; } = (Secret(), $"plain-{Guid.NewGuid():N}");

    /// <summary>Every log entry of both workers.</summary>
    internal LogCollector Logs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _tenant.InitializeAsync();
        _receiver = await WebhookReceiver.StartAsync();

        var urls = new Dictionary<string, ActivationBinding>(ProcessStepFixture.StepAppBindings("a").Bindings, StringComparer.OrdinalIgnoreCase)
        {
            ["HookOneUrl"] = new(ActivationBinding.UrlKind, new Uri(_receiver.BaseUrl + OnePath)),
            ["HookTwoUrl"] = new(ActivationBinding.UrlKind, new Uri(_receiver.BaseUrl + TwoPath)),
            ["HookOneSecret"] = new(ActivationBinding.SecretKind, null),
            ["HookTwoSecret"] = new(ActivationBinding.SecretKind, null),
        };
        (ReleaseId, _model) = await _tenant.ActivateCopyWithFilesAsync(
            new ActivationBindings("a", urls, AllowHttp: true),
            ("webhooks/hook-one.json", Webhook("0b4b1a64-3c3f-4a7d-8c0e-5d1d9a7a1c01", "HookOne", "HookOneUrl", "HookOneSecret")),
            ("webhooks/hook-two.json", Webhook("0b4b1a64-3c3f-4a7d-8c0e-5d1d9a7a1c02", "HookTwo", "HookTwoUrl", "HookTwoSecret")));

        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(_tenant.ConnectionString),
            ["Tenants:a:Bindings:HookOneUrl:Kind"] = "url",
            ["Tenants:a:Bindings:HookOneUrl:Value"] = _receiver.BaseUrl + OnePath,
            ["Tenants:a:Bindings:HookTwoUrl:Kind"] = "url",
            ["Tenants:a:Bindings:HookTwoUrl:Value"] = _receiver.BaseUrl + TwoPath,
            ["Tenants:a:Bindings:HookOneSecret:Kind"] = "secret",
            ["Tenants:a:Bindings:HookOneSecret:Value"] = OneSecret,
            ["Tenants:a:Bindings:HookTwoSecret:Kind"] = "secret",
            ["Tenants:a:Bindings:HookTwoSecret:Value"] = $"{TwoSecrets.Prefixed} {TwoSecrets.Plain}",
        }).Build();
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

        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }

        await _tenant.DisposeAsync();
    }

    /// <summary>
    /// Inserts a request and starts <c>Announce</c> for it, pinned to the release with the webhooks. The step raises
    /// <c>RequestNoted</c> with the ratio <paramref name="amount"/> over <paramref name="divisor"/>, so a divisor of
    /// zero makes the step fail at run time. Returns the request's and the instance's id.
    /// </summary>
    public async Task<(Guid SubjectId, Guid InstanceId)> StartAnnounceAsync(decimal amount, long divisor)
    {
        var subjectId = await _tenant.InsertRequestAsync(amount, divisor);
        var instanceId = await _tenant.StartAsync("Announce", subjectId, ReleaseId, Model);
        return (subjectId, instanceId);
    }

    /// <summary>Runs the next due step item on the Development worker. Returns its outcome, or <see langword="null"/> when there is none.</summary>
    public Task<WorkItemOutcome?> RunStepAsync(CancellationToken cancellationToken) =>
        RunAsync(_development, ProcessStarts.StepWorkItemKind, cancellationToken);

    /// <summary>Runs the next due webhook delivery item on the Development worker.</summary>
    public Task<WorkItemOutcome?> RunDeliveryAsync(CancellationToken cancellationToken) =>
        RunAsync(_development, ProcessStarts.WebhookDeliverWorkItemKind, cancellationToken);

    /// <summary>Runs the next due webhook delivery item on the Production worker.</summary>
    public Task<WorkItemOutcome?> RunDeliveryOutsideDevelopmentAsync(CancellationToken cancellationToken) =>
        RunAsync(_production, ProcessStarts.WebhookDeliverWorkItemKind, cancellationToken);

    /// <summary>Deletes every delivery, its work items and its dead letters, so a test starts with none from an earlier one.</summary>
    public async Task ClearDeliveriesAsync()
    {
        await _tenant.ScalarAsync("DELETE FROM axis.process_work_items WHERE kind = 'webhook.deliver'");
        await _tenant.ScalarAsync("DELETE FROM axis.webhook_deliveries");
        await _tenant.ScalarAsync("DELETE FROM axis.delivery_failures WHERE kind = 'webhook.deliver'");
        await _tenant.ScalarAsync("DELETE FROM axis.dead_letters WHERE kind = 'webhook.deliver'");
    }

    /// <summary>Makes every webhook delivery item due now, so a test need not wait out a backoff.</summary>
    public Task MakeDueAsync() =>
        _tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE kind = 'webhook.deliver'");

    private ServiceProvider Worker(IConfiguration settings, string environment) =>
        new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddSingleton<TimeProvider>(Clock)
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = environment })
            .BuildServiceProvider();

    private async Task<WorkItemOutcome?> RunAsync(ServiceProvider? worker, string kind, CancellationToken cancellationToken)
    {
        var runner = (worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var item = await _tenant.ClaimAsync(kind, _lease, cancellationToken);
        return item is null ? null : await runner.ExecuteAsync(item, cancellationToken);
    }

    private static string Webhook(string id, string name, string url, string secret) =>
        $$"""
        {
          "id": "{{id}}",
          "kind": "webhook",
          "name": "{{name}}",
          "formatVersion": 1,
          "event": "RequestNoted",
          "url": { "binding": "{{url}}" },
          "secret": { "binding": "{{secret}}" },
          "timeout": "PT5S"
        }
        """;

    private static string Secret() => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}
