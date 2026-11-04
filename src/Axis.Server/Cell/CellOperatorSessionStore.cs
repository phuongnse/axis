using System.Buffers.Text;
using System.Security.Cryptography;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// Keeps each <see cref="CellOperatorSession"/> as a row of <c>platform.operator_sessions</c>, so the cookie holds only the
/// key of a session. The key is 32 random bytes in base64url, and the row's key is the SHA-256 hash of those bytes. The row
/// never holds the key or a token. A request reads the row, moves its last request time and refuses it once it is past
/// <see cref="CellOperatorSession.IdleTimeout"/> or <see cref="CellOperatorSession.MaxLifetime"/>. Storing a session first
/// deletes every row that is past either limit, so the table stays small without a cleanup job. The connection is the
/// platform reader role's.
/// </summary>
internal sealed class CellOperatorSessionStore : ITicketStore
{
    private const int IdSize = 32;

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var principal = ticket.Principal;
        var id = principal.FindFirst(CellOperator.IdClaim)?.Value;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("The session holds no operator id.");
        }

        var key = RandomNumberGenerator.GetBytes(IdSize);
        var now = httpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var dataSource = httpContext.RequestServices.GetRequiredService<NpgsqlDataSource>();
        await using var command = dataSource.CreateCommand(
            """
            DELETE FROM platform.operator_sessions WHERE signed_in_at <= @signedInBefore OR last_request_at <= @activeBefore;
            INSERT INTO platform.operator_sessions (id_hash, operator_id, display_name, roles, signed_in_at, last_request_at)
            VALUES (@hash, @id, @name, @roles, @now, @now)
            """);
        command.Parameters.AddWithValue("signedInBefore", now - CellOperatorSession.MaxLifetime);
        command.Parameters.AddWithValue("activeBefore", now - CellOperatorSession.IdleTimeout);
        command.Parameters.AddWithValue("hash", SHA256.HashData(key));
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", principal.FindFirst(CellOperator.DisplayNameClaim)?.Value ?? id);
        command.Parameters.AddWithValue("roles", principal.FindAll(CellOperator.RoleClaim).Select(claim => claim.Value).ToArray());
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Base64Url.EncodeToString(key);
    }

    // The read already moved the last request time, so a renewal has nothing left to write.
    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (Hash(key) is not { } hash)
        {
            return null;
        }

        var dataSource = httpContext.RequestServices.GetRequiredService<NpgsqlDataSource>();

        // Sign-in refuses the session that the request already carries, so it gets a new id.
        if (httpContext.Items.ContainsKey(CellOperatorSession.SigningInItem))
        {
            await DeleteAsync(dataSource, hash, cancellationToken);
            return null;
        }

        var now = httpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        string? id = null, displayName = null;
        string[] roles = [];
        await using (var update = dataSource.CreateCommand(
            """
            UPDATE platform.operator_sessions SET last_request_at = @now
            WHERE id_hash = @hash AND signed_in_at > @signedInAfter AND last_request_at > @activeAfter
            RETURNING operator_id, display_name, roles
            """))
        {
            update.Parameters.AddWithValue("now", now);
            update.Parameters.AddWithValue("hash", hash);
            update.Parameters.AddWithValue("signedInAfter", now - CellOperatorSession.MaxLifetime);
            update.Parameters.AddWithValue("activeAfter", now - CellOperatorSession.IdleTimeout);
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                id = reader.GetString(0);
                displayName = reader.GetString(1);
                roles = reader.GetFieldValue<string[]>(2);
            }
        }

        // No row, or one past a limit. Deleting is harmless when there is no row.
        if (id is null)
        {
            await DeleteAsync(dataSource, hash, cancellationToken);
            return null;
        }

        var principal = CellOperator.CreatePrincipal(id, displayName!, roles);
        // The handler renews a cookie only for a ticket that carries both times.
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + CellOperatorSession.IdleTimeout };
        return new AuthenticationTicket(principal, properties, CellOperatorSession.Scheme);
    }

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (Hash(key) is { } hash)
        {
            await DeleteAsync(httpContext.RequestServices.GetRequiredService<NpgsqlDataSource>(), hash, cancellationToken);
        }
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => throw new NotSupportedException();

    public Task RemoveAsync(string key) => throw new NotSupportedException();

    /// <summary>The hash that keys the row, or null when <paramref name="key"/> is not a session key.</summary>
    private static byte[]? Hash(string key)
    {
        Span<byte> id = stackalloc byte[IdSize];
        return Base64Url.TryDecodeFromChars(key, id, out var written) && written == IdSize
            ? SHA256.HashData(id)
            : null;
    }

    private static async Task DeleteAsync(NpgsqlDataSource dataSource, byte[] hash, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM platform.operator_sessions WHERE id_hash = @hash");
        command.Parameters.AddWithValue("hash", hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
