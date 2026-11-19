using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Stores the in-app notices of a tenant in <c>axis.notifications</c>. A tenant notice has no application and no
/// release, its text keys are platform texts, and its recipient is a platform role that is resolved when the notice is read.
/// </summary>
internal static class TenantNotices
{
    /// <summary>The recipient kind of a notice for a platform role.</summary>
    public const string PlatformRoleRecipient = "platformRole";

    /// <summary>The platform role kind of the tenant's administrators. The server maps it to <c>Users:AdministratorRole</c> when someone reads the notice.</summary>
    public const string AdministratorRole = "administrator";

    /// <summary>The source of a quota notice to the administrators.</summary>
    public const string QuotaSource = "quota";

    public const string EmailsHeldSubjectKey = "notices.emailsHeld.subject";

    public const string EmailsHeldBodyKey = "notices.emailsHeld.body";

    public const string PackagesHeldSubjectKey = "notices.packagesHeld.subject";

    public const string PackagesHeldBodyKey = "notices.packagesHeld.body";

    /// <summary>
    /// Inserts the notice for the administrators that the tenant used up a daily quota of <paramref name="limit"/>, with the platform
    /// texts <paramref name="subjectKey"/> and <paramref name="bodyKey"/> that take <c>limit</c>. It is created at the transaction's time.
    /// </summary>
    public static async Task InsertHeldAsync(NpgsqlTransaction transaction, string subjectKey, string bodyKey, long limit, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types, source, created_at)
            VALUES (@id, NULL, NULL, @kind, @recipient, @subject, @body, @values, @valueTypes, @source, now())
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("source", QuotaSource);
        command.Parameters.AddWithValue("kind", PlatformRoleRecipient);
        command.Parameters.AddWithValue("recipient", AdministratorRole);
        command.Parameters.AddWithValue("subject", subjectKey);
        command.Parameters.AddWithValue("body", bodyKey);
        command.Parameters.Add(new NpgsqlParameter("values", NpgsqlDbType.Jsonb) { Value = $$"""{"limit":{{limit}}}""" });
        command.Parameters.Add(new NpgsqlParameter("valueTypes", NpgsqlDbType.Jsonb) { Value = """{"limit":"integer"}""" });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
