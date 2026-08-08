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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long appliedRevision = 0;
        bool startupComplete = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                (long revision, IReadOnlyList<TrustedPublisherConfigurationKey> keys) = ReadCandidate();
                if (revision == 0 && keys.Count == 0)
                {
                    startupComplete = true;
                }
                else if (revision != appliedRevision)
                {
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    PublisherReconciliationService reconciliation = scope.ServiceProvider
                        .GetRequiredService<PublisherReconciliationService>();
                    await reconciliation.ReconcileAsync(
                        revision,
                        keys,
                        clock.GetUtcNow(),
                        stoppingToken);
                    appliedRevision = revision;
                    startupComplete = true;
                    logger.LogInformation(
                        "Applied trusted publisher configuration revision {ConfigurationRevision}.",
                        revision);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (startupComplete)
            {
                logger.LogError(
                    exception,
                    "Rejected trusted publisher configuration reload; the active ledger is unchanged.");
            }

            await Task.Delay(ReloadInterval, clock, stoppingToken);
        }
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
