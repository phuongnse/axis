using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Server.Authentication;

/// <summary>
/// Keeps and uses the platform client's refresh token of a session, see <c>docs/reference/security.md#sign-in-flow</c>. A sign-in
/// through the platform client on a <c>standard</c> tenant stores the token encrypted in the session's <c>axis.sessions</c> row, and
/// <see cref="RefreshAsync(HttpContext, CancellationToken)"/> turns it into a current access token under a lock on that row. The token
/// is encrypted in the sensitive value format and bound to the row, so it is never in the database in clear and a copy on another row
/// does not decrypt. No response carries it.
/// </summary>
internal sealed partial class SessionTokens(
    TenantSignInSettings settings,
    IOptionsMonitor<OpenIdConnectOptions> oidc,
    SensitiveValues values,
    ILogger<SessionTokens> logger)
{
    /// <summary>The field name in the associated data of a stored token.</summary>
    public const string FieldName = "sessionRefreshToken";

    /// <summary>How long a call to the token endpoint may take. The row stays locked meanwhile, and the session's requests wait for it.</summary>
    public static readonly TimeSpan RefreshLimit = TimeSpan.FromSeconds(10);

    /// <summary>The scope of a session's token: the tenant, an empty entity id and a record id from the first 16 bytes of the id hash.</summary>
    public static SensitiveValueScope Scope(string tenantId, byte[] idHash) => new(tenantId, Guid.Empty, new Guid(idHash.AsSpan(0, 16)));

    /// <summary>
    /// Returns the stored bytes of the token for the session with this id hash, or null when the tenant has no data key. The token is
    /// never stored in clear, so the sign-in then keeps none.
    /// </summary>
    public async Task<byte[]?> EncryptAsync(string tenantId, byte[] idHash, string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            return await values.EncryptTextAsync(Scope(tenantId, idHash), FieldName, refreshToken, cancellationToken);
        }
        catch (TenantDataKeyMissingException)
        {
            LogNoDataKey(logger, tenantId);
            return null;
        }
    }

    /// <summary>
    /// Refreshes the token of the request's session. Null when the request has no session, or as <see cref="RefreshAsync(NpgsqlConnection, string, string?, byte[], CancellationToken)"/> says.
    /// </summary>
    public async Task<string?> RefreshAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var tenant = httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
        var result = await httpContext.AuthenticateAsync(Session.Scheme);
        if (tenant is null
            || !result.Succeeded
            || Session.ReadSessionId(result.Principal) is not { } id
            || !Base64Url.IsValid(id))
        {
            return null;
        }

        var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
        return await RefreshAsync(connection, tenant.TenantId, tenant.SitePath, Base64Url.DecodeFromChars(id), cancellationToken);
    }

    /// <summary>
    /// Refreshes the token of the session with this id hash and returns the new access token. The row is locked while the token
    /// endpoint answers, so two refreshes never rotate the token at once, and the new refresh token the realm returns replaces the
    /// stored one. Null when the session keeps no token, when the tenant is <c>fapi2</c> or unknown, and when the refresh failed. A
    /// token that the realm refuses with <c>invalid_grant</c>, or that no longer decrypts, is cleared. Any other failure keeps it.
    /// </summary>
    public async Task<string?> RefreshAsync(NpgsqlConnection connection, string tenantId, string? sitePath, byte[] idHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (settings.Find(tenantId) is not { } tenant || tenant.Profile == TenantSignInProfiles.Fapi2)
        {
            return null;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        byte[]? stored = null;
        await using (var select = new NpgsqlCommand(
            "SELECT refresh_token FROM axis.sessions WHERE id_hash = @hash AND site_path IS NOT DISTINCT FROM @site FOR UPDATE",
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("hash", idHash);
            select.Parameters.Add(new NpgsqlParameter("site", NpgsqlDbType.Text) { Value = (object?)sitePath ?? DBNull.Value });
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken) && !await reader.IsDBNullAsync(0, cancellationToken))
            {
                stored = reader.GetFieldValue<byte[]>(0);
            }
        }

        if (stored is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        string current;
        try
        {
            current = await values.DecryptTextAsync(Scope(tenantId, idHash), FieldName, stored, cancellationToken);
        }
        catch (SensitiveValueDecryptionException)
        {
            LogNotDecrypted(logger, tenantId);
            await StoreAsync(connection, transaction, idHash, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(RefreshLimit);
            var options = oidc.Get(TenantSignIn.SchemeName(tenantId));
            var configuration = await options.ConfigurationManager!.GetConfigurationAsync(limit.Token);
            using var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = tenant.ClientId!,
                ["client_secret"] = tenant.ClientSecret!.Reveal(),
                ["refresh_token"] = current,
            });
            using var response = await options.Backchannel.PostAsync(configuration.TokenEndpoint, request, limit.Token);
            var body = await response.Content.ReadAsStringAsync(limit.Token);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (response.StatusCode == HttpStatusCode.OK
                && root.TryGetProperty("access_token", out var accessToken)
                && accessToken.GetString() is { Length: > 0 } access)
            {
                if (root.TryGetProperty("refresh_token", out var rotated) && rotated.GetString() is { Length: > 0 } next)
                {
                    await StoreAsync(connection, transaction, idHash, await EncryptAsync(tenantId, idHash, next, cancellationToken), cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return access;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest
                && root.TryGetProperty("error", out var error)
                && error.GetString() == "invalid_grant")
            {
                LogRefused(logger, tenantId);
                await StoreAsync(connection, transaction, idHash, null, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            LogFailed(logger, tenantId, $"status {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException
            && !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, tenantId, exception.GetType().Name);
        }

        await transaction.RollbackAsync(cancellationToken);
        return null;
    }

    private static async Task StoreAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, byte[] idHash, byte[]? token, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE axis.sessions SET refresh_token = @token WHERE id_hash = @hash", connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("token", NpgsqlDbType.Bytea) { Value = (object?)token ?? DBNull.Value });
        command.Parameters.AddWithValue("hash", idHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Warning, "Tenant {TenantId} has no data key, so a sign-in keeps no refresh token. Token exchange has nothing to work with there.")]
    private static partial void LogNoDataKey(ILogger logger, string tenantId);

    [LoggerMessage(LogLevel.Warning, "A session's stored refresh token of tenant {TenantId} does not decrypt, so it was cleared.")]
    private static partial void LogNotDecrypted(ILogger logger, string tenantId);

    [LoggerMessage(LogLevel.Information, "The identity provider of tenant {TenantId} refused a session's refresh token, so it was cleared.")]
    private static partial void LogRefused(ILogger logger, string tenantId);

    [LoggerMessage(LogLevel.Warning, "Refreshing a session's token of tenant {TenantId} failed with {Reason}. The stored token is kept.")]
    private static partial void LogFailed(ILogger logger, string tenantId, string reason);
}
