using System.Text.Json;
using Axis.Configuration.Model;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Storage;

/// <summary>
/// Stores the in-app notices of an AI request in <c>axis.notifications</c>. An AI request notice goes to one person, the request's
/// author, and has no application id and no release, because its text keys are platform texts. It opens the request in Studio,
/// see <c>docs/reference/notifications.md#tenant-notices</c>.
/// </summary>
public static class AuthoringRequestNotices
{
    /// <summary>The source of an AI request notice.</summary>
    public const string Source = "authoringRequest";

    /// <summary>The recipient kind of a notice for one person. The recipient is the person's user id.</summary>
    public const string PlatformUserRecipient = "platformUser";

    public const string PlanReadySubjectKey = "notices.authoringPlanReady.subject";

    public const string PlanReadyBodyKey = "notices.authoringPlanReady.body";

    public const string DraftReadySubjectKey = "notices.authoringDraftReady.subject";

    public const string DraftReadyBodyKey = "notices.authoringDraftReady.body";

    public const string AcceptedSubjectKey = "notices.authoringAccepted.subject";

    public const string AcceptedBodyKey = "notices.authoringAccepted.body";

    public const string FailedSubjectKey = "notices.authoringFailed.subject";

    public const string FailedBodyKey = "notices.authoringFailed.body";

    /// <summary>The Studio page that a notice opens.</summary>
    private const string StudioPage = "studio";

    /// <summary>
    /// Inserts the notice <paramref name="notice"/> for <paramref name="author"/>, the author of the request <paramref name="requestId"/>
    /// in the application <paramref name="application"/>. It is created at the transaction's time, so it commits with the move of the request.
    /// </summary>
    public static async Task InsertAsync(
        NpgsqlTransaction transaction,
        AuthoringRequestNotice notice,
        Guid requestId,
        string author,
        string application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrEmpty(author);
        ArgumentNullException.ThrowIfNull(application);

        var (kind, subjectKey, bodyKey) = notice switch
        {
            AuthoringRequestNotice.PlanReady => (NotificationKinds.Action, PlanReadySubjectKey, PlanReadyBodyKey),
            AuthoringRequestNotice.DraftReady => (NotificationKinds.Action, DraftReadySubjectKey, DraftReadyBodyKey),
            AuthoringRequestNotice.Accepted => (NotificationKinds.Info, AcceptedSubjectKey, AcceptedBodyKey),
            AuthoringRequestNotice.Failed => (NotificationKinds.Warning, FailedSubjectKey, FailedBodyKey),
            _ => throw new ArgumentOutOfRangeException(nameof(notice), notice, "Unknown AI request notice."),
        };
        var request = requestId.ToString("D");

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types,
                 open_page, open_parameters, source, kind, created_at)
            VALUES (@id, NULL, NULL, @recipientKind, @recipient, @subject, @body, @values, @valueTypes,
                    @openPage, @openParameters, @source, @notificationKind, now())
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("recipientKind", PlatformUserRecipient);
        command.Parameters.AddWithValue("recipient", author);
        command.Parameters.AddWithValue("subject", subjectKey);
        command.Parameters.AddWithValue("body", bodyKey);
        command.Parameters.AddWithValue("source", Source);
        command.Parameters.AddWithValue("notificationKind", kind);
        command.Parameters.AddWithValue("openPage", StudioPage);
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new Dictionary<string, string> { ["application"] = application, ["request"] = request }),
        });
        command.Parameters.Add(new NpgsqlParameter("valueTypes", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new Dictionary<string, string> { ["application"] = "text", ["request"] = "text" }),
        });
        command.Parameters.Add(new NpgsqlParameter("openParameters", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(new Dictionary<string, string> { ["app"] = application, ["view"] = "requests", ["request"] = request }),
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>The four moves of an AI request that its author is told about.</summary>
public enum AuthoringRequestNotice
{
    /// <summary>The request has a plan to approve. An <c>action</c> notice.</summary>
    PlanReady,

    /// <summary>The request has a draft to accept. An <c>action</c> notice.</summary>
    DraftReady,

    /// <summary>AI accepted the draft. An <c>info</c> notice.</summary>
    Accepted,

    /// <summary>The request stopped. A <c>warning</c> notice.</summary>
    Failed,
}
