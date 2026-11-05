using Axis.ControlPlane.Emails;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Axis.ControlPlane.Health;

/// <summary>
/// Reports whether the mail relay accepts a connection. It signs in with nothing and sends no email. A failure is reported with
/// the registration's failure status, so the registration decides whether it is Unhealthy or Degraded, and the message holds no
/// host or credential.
/// </summary>
internal sealed class MailRelayHealthCheck(ControlPlaneOptions options) : IHealthCheck
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = await MailRelay.ConnectAsync(options.Mail, _timeout, authenticate: false, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The mail relay does not accept a connection.", exception);
        }
    }
}
