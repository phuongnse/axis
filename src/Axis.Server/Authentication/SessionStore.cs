using System.Buffers.Text;
using System.Security.Cryptography;
using Axis.Core.Tenancy;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Server.Authentication;

/// <summary>
/// Keeps each <see cref="Session"/> as a row of <c>axis.sessions</c> in the tenant database, so the
/// cookie holds only the key of a session. The key is 32 random bytes in base64url, and the row's key
/// is the SHA-256 hash of those bytes. The row never holds the key or a token. A request reads the row,
/// moves its last request time and refuses it once it is past <see cref="Session.IdleTimeout"/> or
/// <see cref="Session.MaxLifetime"/>. The row holds the site path of the host that made it, null for the tenant's own
/// hosts, and a request on another host of the tenant neither uses nor deletes it. The overloads without an <see cref="HttpContext"/> cannot know the
/// tenant, so they are not supported.
/// </summary>
internal sealed class SessionStore : ITicketStore
{
    private const int IdSize = 32;

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var tenant = httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current
            ?? throw new InvalidOperationException("No tenant context is set.");
        var user = Session.Read(ticket.Principal);
        if (user is null || Session.ReadTenantId(ticket.Principal) != tenant.TenantId)
        {
            throw new InvalidOperationException("The session belongs to another tenant than the request.");
        }

        var id = RandomNumberGenerator.GetBytes(IdSize);
        var now = httpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.sessions (id_hash, user_id, display_name, roles, signed_in_at, last_request_at, authenticated_at, site_path)
            VALUES (@hash, @user, @name, @roles, @now, @now, @authenticatedAt, @site)
            """,
            connection);
        command.Parameters.AddWithValue("hash", SHA256.HashData(id));
        command.Parameters.AddWithValue("user", user.Id);
        command.Parameters.AddWithValue("name", user.DisplayName);
        command.Parameters.AddWithValue("roles", user.Roles.ToArray());
        command.Parameters.AddWithValue("now", now);
        command.Parameters.Add(new NpgsqlParameter("authenticatedAt", NpgsqlDbType.TimestampTz)
        {
            Value = Session.ReadAuthenticatedAt(ticket.Principal) is { } authenticatedAt ? authenticatedAt : DBNull.Value,
        });
        command.Parameters.Add(SiteParameter(tenant));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Base64Url.EncodeToString(id);
    }

    // The read already moved the last request time, so a renewal has nothing left to write.
    public Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current is not { } tenant
            || Hash(key) is not { } hash)
        {
            return null;
        }

        var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);

        // Sign-in refuses the session that the request already carries, so it gets a new id.
        if (httpContext.Items.ContainsKey(Session.SigningInItem))
        {
            await DeleteAsync(connection, hash, tenant, cancellationToken);
            return null;
        }

        var now = httpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        await using (var update = new NpgsqlCommand(
            """
            UPDATE axis.sessions SET last_request_at = @now
            WHERE id_hash = @hash AND site_path IS NOT DISTINCT FROM @site AND signed_in_at > @signedInAfter AND last_request_at > @activeAfter
            RETURNING user_id, display_name, roles, authenticated_at
            """,
            connection))
        {
            update.Parameters.AddWithValue("now", now);
            update.Parameters.AddWithValue("hash", hash);
            update.Parameters.AddWithValue("signedInAfter", now - Session.MaxLifetime);
            update.Parameters.AddWithValue("activeAfter", now - Session.IdleTimeout);
            update.Parameters.Add(SiteParameter(tenant));
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var principal = Session.CreatePrincipal(
                    tenant.TenantId,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetFieldValue<string[]>(2),
                    await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                    Base64Url.EncodeToString(hash));
                // The handler renews a cookie only for a ticket that carries both times.
                var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + Session.IdleTimeout };
                return new AuthenticationTicket(principal, properties, Session.Scheme);
            }
        }

        // No row, or one past a limit. Deleting is harmless when there is no row, and it leaves a row of another host alone.
        await DeleteAsync(connection, hash, tenant, cancellationToken);
        return null;
    }

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current is not { } tenant
            || Hash(key) is not { } hash)
        {
            return;
        }

        var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        await DeleteAsync(connection, hash, tenant, cancellationToken);
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

    /// <summary>The site of the host that the request came on, null for the tenant's own hosts.</summary>
    private static NpgsqlParameter SiteParameter(TenantContext tenant) =>
        new("site", NpgsqlDbType.Text) { Value = (object?)tenant.SitePath ?? DBNull.Value };

    private static async Task DeleteAsync(NpgsqlConnection connection, byte[] hash, TenantContext tenant, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.sessions WHERE id_hash = @hash AND site_path IS NOT DISTINCT FROM @site",
            connection);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.Add(SiteParameter(tenant));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
