using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;
using MailKit.Net.Smtp;
using MimeKit;

namespace Axis.ControlPlane.Emails;

/// <summary>
/// Runs a <c>sendEmail</c> job: fills in the template of the payload in the person's language and sends it as plain text through
/// the control plane's own mail relay, from <c>ControlPlane:MailFrom</c>, never through a cell's mail settings. The
/// <c>Message-ID</c> comes from the job id, so a job that runs twice after a crash sends two copies that a receiver can
/// recognise. A failed send throws an error that holds the type of the failure and never its message, since an SMTP rejection
/// can quote the recipient's address, and <c>last_error</c> must hold none.
/// </summary>
internal sealed class SendEmailJobHandler(ControlPlaneOptions options, EmailTemplates templates) : IJobHandler
{
    // Well inside the lease of 5 minutes.
    private static readonly TimeSpan _sendTimeout = TimeSpan.FromSeconds(60);

    public string Kind => EmailJobs.Kind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var message = Build(job);
        try
        {
            using var client = await MailRelay.ConnectAsync(options.Mail, _sendTimeout, authenticate: true, cancellationToken);
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

    private MimeMessage Build(ClaimedJob job)
    {
        var payload = job.Payload;
        var template = Text(payload, "template");
        var language = Text(payload, "language");
        var to = Text(payload, "to");
        var values = (payload["values"] as JsonObject ?? [])
            .ToDictionary(value => value.Key, value => value.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal);
        var (subject, body) = templates.Render(template, language, values);

        var from = MailboxAddress.Parse(options.MailFrom!);
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain") { Text = body },

            // MimeKit adds the angle brackets itself.
            MessageId = $"{job.Id:N}@{from.Domain}",
        };
        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(to));
        return message;
    }

    private static string Text(JsonObject payload, string name) =>
        payload[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"The sendEmail payload has no {name}.");
}
