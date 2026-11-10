using System.Net;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// Appends to <c>control_plane.terms_acceptances</c>, see <c>docs/reference/sign-up.md#terms-acceptance</c>. There is no update or
/// delete method, and the database rejects both. An acceptance is appended in the transaction of the confirm it belongs to, so both
/// commit or neither does.
/// </summary>
internal static class TermsAcceptances
{
    public const string Terms = "terms";

    public const string Dpa = "dpa";

    /// <summary>The longest user agent that is kept. A longer one is cut, as the rows can never be removed.</summary>
    public const int MaxUserAgentLength = 512;

    /// <summary>
    /// Appends the acceptance of <paramref name="document"/> at <paramref name="version"/> by <paramref name="account"/> for the
    /// tenant <paramref name="tenantId"/>. The address and the user agent are null when the request had none.
    /// </summary>
    public static async Task AppendAsync(
        NpgsqlTransaction transaction,
        string account,
        string tenantId,
        string document,
        string version,
        string privacyVersion,
        IPAddress? clientAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO control_plane.terms_acceptances (id, account, tenant_id, document, version, privacy_version, client_address, user_agent)
            VALUES (@id, @account, @tenantId, @document, @version, @privacyVersion, @clientAddress, @userAgent)
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("account", account);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("document", document);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("privacyVersion", privacyVersion);
        command.Parameters.Add(new NpgsqlParameter("clientAddress", NpgsqlTypes.NpgsqlDbType.Inet) { Value = (object?)clientAddress ?? DBNull.Value });
        command.Parameters.AddWithValue("userAgent", (object?)(userAgent?.Length > MaxUserAgentLength ? userAgent[..MaxUserAgentLength] : userAgent) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
