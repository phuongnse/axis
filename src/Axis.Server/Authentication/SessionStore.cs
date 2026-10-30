using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Core.Tenancy;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Axis.Server.Users;
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
/// <see cref="Session.MaxLifetime"/>. The row holds the alias of the provider that signed the person in, null for accounts, and the roles that each application's provider mapping gave the person, by application id. It never holds the roles that an administrator gave: the same statement that moves the last request time reads them from <c>axis.given_roles</c>, and they live in the request's principal only, so a change applies at the next request on every replica. A given platform role is stored by its kind, and the request maps it to the role name that the host setting configures, which joins the roles of the sign-in. It holds the site path of the host that made it, null for the tenant's own
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
            INSERT INTO axis.sessions (id_hash, user_id, display_name, roles, signed_in_at, last_request_at, authenticated_at, site_path, identity_provider, application_roles)
            VALUES (@hash, @user, @name, @roles, @now, @now, @authenticatedAt, @site, @identityProvider, @applicationRoles)
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
        command.Parameters.Add(new NpgsqlParameter("identityProvider", NpgsqlDbType.Text)
        {
            Value = (object?)Session.ReadIdentityProvider(ticket.Principal) ?? DBNull.Value,
        });
        command.Parameters.Add(new NpgsqlParameter("applicationRoles", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(user.ApplicationRoles) });
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
            WITH touched AS (
                UPDATE axis.sessions SET last_request_at = @now
                WHERE id_hash = @hash AND site_path IS NOT DISTINCT FROM @site AND signed_in_at > @signedInAfter AND last_request_at > @activeAfter
                RETURNING user_id, display_name, roles, authenticated_at, identity_provider, application_roles::text AS application_roles
            )
            SELECT t.user_id, t.display_name, t.roles, t.authenticated_at, t.identity_provider, t.application_roles,
                ARRAY(SELECT g.application_id FROM axis.given_roles g WHERE g.user_id = t.user_id AND g.application_id IS NOT NULL ORDER BY g.application_id, g.role),
                ARRAY(SELECT g.role FROM axis.given_roles g WHERE g.user_id = t.user_id AND g.application_id IS NOT NULL ORDER BY g.application_id, g.role),
                ARRAY(SELECT g.role FROM axis.given_roles g WHERE g.user_id = t.user_id AND g.application_id IS NULL ORDER BY g.role)
            FROM touched t
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
                var applicationRoles = JsonSerializer.Deserialize<Dictionary<Guid, string[]>>(reader.GetString(5))!
                    .ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
                MergeGivenRoles(applicationRoles, reader.GetFieldValue<Guid[]>(6), reader.GetFieldValue<string[]>(7));
                var services = httpContext.RequestServices;
                var studio = services.GetRequiredService<StudioOptions>();
                var console = services.GetRequiredService<ConsoleSettings>();
                var users = services.GetRequiredService<UsersSettings>();
                var givenPlatformRoles = reader.GetFieldValue<string[]>(8)
                    .Select(kind => PlatformRoles.NameOf(kind, studio, console, users))
                    .OfType<string>();
                var principal = Session.CreatePrincipal(
                    tenant.TenantId,
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetFieldValue<string[]>(2).Concat(givenPlatformRoles).Distinct(StringComparer.Ordinal),
                    await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                    Base64Url.EncodeToString(hash),
                    await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                    applicationRoles);
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

    /// <summary>
    /// Adds the roles that an administrator gave to the roles that the row holds, without repeats. They live in the principal of
    /// this request only, never in the row.
    /// </summary>
    private static void MergeGivenRoles(Dictionary<Guid, IReadOnlyList<string>> applicationRoles, Guid[] applicationIds, string[] roles)
    {
        for (var index = 0; index < applicationIds.Length; index++)
        {
            var held = applicationRoles.TryGetValue(applicationIds[index], out var existing) ? existing : [];
            if (!held.Contains(roles[index], StringComparer.Ordinal))
            {
                applicationRoles[applicationIds[index]] = [.. held, roles[index]];
            }
        }
    }

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
