using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Axis.ControlPlane.Emails;

/// <summary>Opens connections to the control plane's mail relay, <c>ControlPlane:Mail</c>. The caller disposes the client.</summary>
internal static class MailRelay
{
    // Well inside the lease of 5 minutes.
    private static readonly TimeSpan _sendTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Fills in the template in the person's language and sends it as plain text through the control plane's own mail relay, from
    /// <c>ControlPlane:MailFrom</c>, never through a cell's mail settings. The <c>Message-ID</c> comes from
    /// <paramref name="messageId"/>, so a job that runs twice after a crash sends two copies that a receiver can recognise. A
    /// failed send throws an error that holds the type of the failure and never its message, since an SMTP rejection can quote
    /// the recipient's address, and <c>last_error</c> must hold none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The relay did not take the email.</exception>
    public static async Task SendAsync(
        ControlPlaneOptions options,
        EmailTemplates templates,
        Guid messageId,
        string template,
        string language,
        string to,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(values);
        var (subject, body) = templates.Render(template, language, values);

        var from = MailboxAddress.Parse(options.MailFrom!);
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain") { Text = body },

            // MimeKit adds the angle brackets itself.
            MessageId = $"{messageId:N}@{from.Domain}",
        };
        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(to));
        try
        {
            using var client = await ConnectAsync(options.Mail, _sendTimeout, authenticate: true, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            try
            {
                await client.DisconnectAsync(quit: true, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The relay has the email, and the client is disposed next.
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"The mail relay did not take the email: {exception.GetType().Name}.");
        }
    }

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
