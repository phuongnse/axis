using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;
using MimeKit;
using Npgsql;

namespace Axis.ControlPlane.Emails;

/// <summary>
/// Queues the <c>sendEmail</c> jobs, the only way code sends an email. The payload is <c>{ template, language, to, values }</c>
/// and it is stored in the database, so it never holds a password, a token or a payment detail: an email that needs one needs
/// its own design. A bad email fails when it is queued and not on the tenth retry.
/// </summary>
internal static class EmailJobs
{
    public const string Kind = "sendEmail";

    /// <summary>
    /// Queues one email and returns the id of its job. Pass the <paramref name="transaction"/> of the change that needs the email,
    /// so both commit or neither does.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The template is unknown, the language is not one of <see cref="EmailTemplates.Languages"/>, the address is not an email
    /// address, or <paramref name="values"/> does not hold a value for each placeholder of the template.
    /// </exception>
    public static async Task<Guid> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        EmailTemplates templates,
        string template,
        string language,
        string to,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(values);

        // Renders once to check the template, the language and the values. Nothing of the result is kept.
        _ = templates.Render(template, language, values);
        if (string.IsNullOrWhiteSpace(to) || !MailboxAddress.TryParse(to, out _))
        {
            throw new ArgumentException("The recipient is not an email address.", nameof(to));
        }

        var payload = new JsonObject
        {
            ["template"] = template,
            ["language"] = language,
            ["to"] = to,
            ["values"] = new JsonObject(values.Select(value => KeyValuePair.Create(value.Key, (JsonNode?)JsonValue.Create(value.Value)))),
        };
        return await JobQueue.EnqueueAsync(connection, transaction, Kind, payload, null, cancellationToken);
    }
}
