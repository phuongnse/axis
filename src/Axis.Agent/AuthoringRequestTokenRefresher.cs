using Axis.Data.Records;
using Axis.Data.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Agent;

/// <summary>
/// Gives a step the author's current access token, see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>. It reads the
/// request's stored refresh token, refreshes it at the tenant's realm as the agent client and stores the new refresh token the realm
/// returns. A token that the realm refuses with <c>invalid_grant</c>, a request with no token and a token that no longer decrypts have
/// ended, and the refresher says so with <see cref="AuthoringTokenEndedException"/>. Any other failure, such as a tenant with no agent
/// client or a realm that does not answer, throws as it is, so the request keeps its token and the next claim runs the step again. Its logs
/// name the tenant and the request, never a token.
/// </summary>
internal sealed partial class AuthoringRequestTokenRefresher(
    AuthoringRequestStore store,
    SensitiveValues values,
    ITenantCatalog catalog,
    IHttpClientFactory httpClients,
    ILogger<AuthoringRequestTokenRefresher> logger)
{
    /// <summary>The name of the HTTP client that calls a tenant's realm.</summary>
    public const string HttpClientName = "AgentRealm";

    /// <exception cref="AuthoringTokenEndedException">The realm refused the token, or the request keeps none, or it no longer decrypts.</exception>
    /// <exception cref="AuthoringClaimLostException">The host no longer holds the claim.</exception>
    /// <exception cref="InvalidOperationException">The tenant has no agent client, or the realm answered with an error.</exception>
    public async Task<string> AccessTokenAsync(string tenantId, Guid requestId, string claimedBy, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        if (catalog.Find(tenantId)?.RealmAgent is not { } agent)
        {
            throw new InvalidOperationException($"Tenant '{tenantId}' has no agent client, so the agent host cannot refresh a token at its realm.");
        }

        if (await store.ReadTokenAsync(tenantId, requestId, claimedBy, cancellationToken) is not { } stored)
        {
            LogMissing(tenantId, requestId);
            throw new AuthoringTokenEndedException();
        }

        string current;
        try
        {
            current = await AuthoringRequestTokens.DecryptAsync(values, tenantId, requestId, stored, cancellationToken);
        }
        catch (SensitiveValueDecryptionException)
        {
            LogNotDecrypted(tenantId, requestId);
            throw new AuthoringTokenEndedException();
        }

        RealmAgentExchange refreshed;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            limit.CancelAfter(RealmAgentTokens.RefreshLimit);
            refreshed = await new RealmAgentTokens(httpClients.CreateClient(HttpClientName), agent).RefreshAsync(current, limit.Token);
        }

        if (refreshed.IsRefused)
        {
            LogRefused(tenantId, requestId);
            throw new AuthoringTokenEndedException();
        }

        await store.StoreTokenAsync(tenantId, requestId, claimedBy, values, refreshed.RefreshToken!, cancellationToken);
        return refreshed.AccessToken!;
    }

    [LoggerMessage(LogLevel.Information, "The realm of tenant {TenantId} refused the token of authoring request {RequestId}.")]
    private partial void LogRefused(string tenantId, Guid requestId);

    [LoggerMessage(LogLevel.Information, "Authoring request {RequestId} of tenant {TenantId} keeps no token.")]
    private partial void LogMissing(string tenantId, Guid requestId);

    [LoggerMessage(LogLevel.Warning, "The token of authoring request {RequestId} of tenant {TenantId} no longer decrypts.")]
    private partial void LogNotDecrypted(string tenantId, Guid requestId);
}
