using Axis.Data.Records;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Storage;

/// <summary>
/// Keeps the agent host's refresh token on an authoring request's row, see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>.
/// The token is encrypted with the tenant data key in the sensitive value format, bound to the tenant and the request, so it is never
/// in the database in clear. No route answers it, and a drop, an accept or a failure erases it.
/// </summary>
public static class AuthoringRequestTokens
{
    /// <summary>The field name in the associated data of a stored token.</summary>
    public const string FieldName = "refreshToken";

    /// <summary>The scope of a request's token: the tenant, an empty entity id and the request id.</summary>
    public static SensitiveValueScope Scope(string tenantId, Guid requestId) => new(tenantId, Guid.Empty, requestId);

    /// <summary>Decrypts a stored token of the request.</summary>
    /// <exception cref="SensitiveValueDecryptionException">The value does not decrypt: its key is gone, or it was written for another tenant or request.</exception>
    public static Task<string> DecryptAsync(SensitiveValues values, string tenantId, Guid requestId, byte[] stored, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(stored);

        return values.DecryptTextAsync(Scope(tenantId, requestId), FieldName, stored, cancellationToken);
    }

    /// <summary>
    /// Encrypts the token and stores it on the request, replacing any token there. Answers false, and stores nothing, when the request
    /// does not exist or has ended: <c>accepted</c>, <c>dropped</c> or <c>failed</c>.
    /// </summary>
    public static Task<bool> StoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SensitiveValues values,
        string tenantId,
        Guid requestId,
        string refreshToken,
        CancellationToken cancellationToken = default) =>
        StoreAsync(connection, transaction, values, tenantId, requestId, refreshToken, null, cancellationToken);

    /// <summary>
    /// Stores the token as the overload without a claim does, and answers false also when <paramref name="claimedBy"/> is set and that
    /// host does not hold the claim on the request.
    /// </summary>
    public static async Task<bool> StoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SensitiveValues values,
        string tenantId,
        Guid requestId,
        string refreshToken,
        string? claimedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var stored = await values.EncryptTextAsync(Scope(tenantId, requestId), FieldName, refreshToken, cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE axis.authoring_requests SET refresh_token = @token WHERE id = @id AND state NOT IN ('accepted', 'dropped', 'failed') AND (@me IS NULL OR claimed_by = @me)",
            connection,
            transaction);
        command.Parameters.Add(new NpgsqlParameter("token", NpgsqlDbType.Bytea) { Value = stored });
        command.Parameters.AddWithValue("id", requestId);
        command.Parameters.Add(new NpgsqlParameter("me", NpgsqlDbType.Text) { Value = (object?)claimedBy ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
