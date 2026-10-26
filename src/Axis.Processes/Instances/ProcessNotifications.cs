using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the in-app notifications that <c>notify</c> steps store over a connection to a tenant
/// database: one row for each user or role recipient, all with the notification's id.
/// </summary>
internal static class ProcessNotifications
{
    /// <summary>The kind of a recipient that is one user, by user id.</summary>
    internal const string UserRecipient = "user";

    /// <summary>The kind of a recipient that is every user with one role.</summary>
    internal const string RoleRecipient = "role";

    /// <summary>Inserts a row of <paramref name="notification"/> for each of its recipients. They are created at the transaction's time.</summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, NewNotification notification, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", open_page, open_parameters, created_at)
            SELECT @id, @application, @release, recipient.kind, recipient.name, @subject, @body, @values, @page, @parameters, now()
            FROM unnest(@kinds, @recipients) AS recipient(kind, name)
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", notification.Id);
        command.Parameters.AddWithValue("application", notification.ApplicationId);
        command.Parameters.AddWithValue("release", notification.ReleaseId);
        command.Parameters.AddWithValue("subject", notification.SubjectKey);
        command.Parameters.AddWithValue("body", notification.BodyKey);
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Jsonb) { Value = notification.Values.ToJsonString() });
        command.Parameters.Add(new NpgsqlParameter("page", NpgsqlDbType.Text) { Value = (object?)notification.OpenPage ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("parameters", NpgsqlDbType.Jsonb) { Value = (object?)notification.OpenParameters?.ToJsonString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = notification.Recipients.Select(r => r.Kind).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("recipients", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = notification.Recipients.Select(r => r.Recipient).ToArray() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>An in-app notification that a <c>notify</c> step stores for its recipients.</summary>
/// <param name="Id">The notification's id, a version 7 UUID. Every recipient's row has it.</param>
/// <param name="ReleaseId">The release the notifying instance is pinned to.</param>
/// <param name="SubjectKey">The text key of the subject.</param>
/// <param name="BodyKey">The text key of the body.</param>
/// <param name="Values">The raw evaluated placeholder values, keyed by the placeholders' names.</param>
/// <param name="OpenPage">The declared name of the page the notification opens, or null.</param>
/// <param name="OpenParameters">The raw evaluated page parameters, keyed by the parameters' declared names, or null when there is no page.</param>
/// <param name="Recipients">The distinct user and role recipients, each by kind and by user id or role name.</param>
internal sealed record NewNotification(
    Guid Id,
    Guid ApplicationId,
    Guid ReleaseId,
    string SubjectKey,
    string BodyKey,
    JsonObject Values,
    string? OpenPage,
    JsonObject? OpenParameters,
    IReadOnlyList<(string Kind, string Recipient)> Recipients);
