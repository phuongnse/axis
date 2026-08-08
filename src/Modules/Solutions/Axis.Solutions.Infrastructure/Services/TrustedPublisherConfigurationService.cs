using Axis.Solutions.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.Solutions.Infrastructure.Services;

public sealed class TrustedPublisherConfigurationService(
    IConfiguration configuration,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<TrustedPublisherConfigurationService> logger) : BackgroundService
{
    private static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(5);
    private long _appliedRevision = -1;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReconcileCandidateAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(ReloadInterval, clock, stoppingToken);
            try
            {
                await ReconcileCandidateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Rejected trusted publisher configuration reload; the active ledger is unchanged.");
            }
        }
    }

    private async Task ReconcileCandidateAsync(CancellationToken cancellationToken)
    {
        (long revision, IReadOnlyList<TrustedPublisherConfigurationKey> keys) = ReadCandidate();
        if (revision == _appliedRevision)
            return;
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        PublisherReconciliationService reconciliation = scope.ServiceProvider
            .GetRequiredService<PublisherReconciliationService>();
        await reconciliation.ReconcileAsync(revision, keys, clock.GetUtcNow(), cancellationToken);
        _appliedRevision = revision;
        logger.LogInformation(
            "Applied trusted publisher configuration revision {ConfigurationRevision}.",
            revision);
    }

    private (long Revision, IReadOnlyList<TrustedPublisherConfigurationKey> Keys) ReadCandidate()
    {
        IConfigurationSection section = configuration.GetSection("Solutions:TrustedPublishers");
        long revision = section.GetValue<long>("ConfigurationRevision");
        List<TrustedPublisherConfigurationKey> keys = [];
        foreach (IConfigurationSection value in section.GetSection("Keys").GetChildren())
        {
            keys.Add(new TrustedPublisherConfigurationKey(
                Required(value, "PublisherId"),
                Required(value, "KeyId"),
                Required(value, "PublicKeyPem"),
                value.GetValue<bool>("IsActive")));
        }
        if (keys.Count > 0 && revision <= 0)
            throw new InvalidOperationException("solutions.publisher_configuration.invalid");
        return (revision, keys);
    }

    private static string Required(IConfiguration section, string key) =>
        string.IsNullOrWhiteSpace(section[key])
            ? throw new InvalidOperationException("solutions.publisher_configuration.invalid")
            : section[key]!.Trim();
}
