using MailKit.Net.Smtp;
using MailKit.Security;

namespace Axis.ControlPlane.Emails;

/// <summary>Opens connections to the control plane's mail relay, <c>ControlPlane:Mail</c>. The caller disposes the client.</summary>
internal static class MailRelay
{
    /// <summary>
    /// Returns a client connected to the relay, and signed in when <paramref name="authenticate"/> is set and the settings have a
    /// user name. <paramref name="timeout"/> bounds every step. TLS is plain only when the settings turn it off, which the start
    /// allows in Development and Testing alone: port 465 starts with TLS, any other port upgrades with STARTTLS.
    /// </summary>
    public static async Task<SmtpClient> ConnectAsync(ControlPlaneMailOptions options, TimeSpan timeout, bool authenticate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = new SmtpClient { Timeout = (int)timeout.TotalMilliseconds };
        try
        {
            var security = !options.UseTls
                ? SecureSocketOptions.None
                : options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(options.Host!, options.Port, security, cancellationToken);
            if (authenticate && !string.IsNullOrEmpty(options.UserName))
            {
                await client.AuthenticateAsync(options.UserName, options.Password ?? string.Empty, cancellationToken);
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
