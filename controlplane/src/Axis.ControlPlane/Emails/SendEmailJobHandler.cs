using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;

namespace Axis.ControlPlane.Emails;

/// <summary>
/// Runs a <c>sendEmail</c> job: reads the payload and sends the email through <see cref="MailRelay.SendAsync"/>, which fills in the
/// template of the payload in the person's language and sends it as plain text through the control plane's own mail relay, from
/// <c>ControlPlane:MailFrom</c>, never through a cell's mail settings. The <c>Message-ID</c> comes from the job id, so a job that
/// runs twice after a crash sends two copies that a receiver can recognise. A failed send throws an error that holds the type of
/// the failure and never its message, since an SMTP rejection can quote the recipient's address, and <c>last_error</c> must hold none.
/// </summary>
internal sealed class SendEmailJobHandler(ControlPlaneOptions options, EmailTemplates templates) : IJobHandler
{
    public string Kind => EmailJobs.Kind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var payload = job.Payload;
        var values = (payload["values"] as JsonObject ?? [])
            .ToDictionary(value => value.Key, value => value.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal);
        await MailRelay.SendAsync(
            options,
            templates,
            job.Id,
            Text(payload, "template"),
            Text(payload, "language"),
            Text(payload, "to"),
            values,
            cancellationToken);
    }

    private static string Text(JsonObject payload, string name) =>
        payload[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"The sendEmail payload has no {name}.");
}
