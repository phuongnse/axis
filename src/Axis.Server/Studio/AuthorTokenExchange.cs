using System.Text.Json;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Studio;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Axis.Server.Studio;

/// <summary>
/// Gets the agent host's token for the author who sends an authoring request, see
/// <c>docs/reference/authoring-requests.md#acting-for-the-author</c>.
/// </summary>
internal interface IAuthorTokenExchange
{
    /// <summary>
    /// Refreshes the request's session token, trades it at the tenant's realm for the agent host's token and checks that the new token
    /// carries the author role. It writes nothing.
    /// </summary>
    Task<AuthorTokenExchangeResult> ExchangeAsync(HttpContext httpContext, CancellationToken cancellationToken);
}

/// <summary>How a token exchange ended.</summary>
internal enum AuthorTokenOutcome
{
    /// <summary>The exchange gave a refresh token of the agent host for an author.</summary>
    Exchanged,

    /// <summary>The tenant cannot let the agent host act: it is <c>fapi2</c>, or has no agent client.</summary>
    NotOffered,

    /// <summary>The session keeps no platform token, or the realm refused the grant. The person signs in again.</summary>
    Refused,

    /// <summary>The exchanged token does not carry the author role.</summary>
    NotAuthor,

    /// <summary>The realm did not answer, or answered an error that is not a refusal.</summary>
    Unavailable,
}

/// <summary>The outcome of a token exchange. <c>RefreshToken</c> is set only for <see cref="AuthorTokenOutcome.Exchanged"/>.</summary>
internal sealed record AuthorTokenExchangeResult(AuthorTokenOutcome Outcome, string? RefreshToken = null);

/// <summary>
/// The exchange of <see cref="IAuthorTokenExchange"/>. It refreshes the session's platform token with <see cref="SessionTokens"/>, trades
/// it with <see cref="RealmAgentTokens"/> and reads the roles from the exchanged token's roles claim, the tenant's <c>Claims:Roles</c>,
/// the same way <c>/mcp</c> does for the agent host's token. Only the realm's answer of 400 counts as a refused grant. A wrong agent
/// secret, a realm that is down and a timeout are not the person's to fix, so they are unavailable. A log line names the tenant and the
/// outcome, never a token.
/// </summary>
internal sealed partial class AuthorTokenExchange(
    TenantSignInSettings settings,
    ITenantCatalog catalog,
    SessionTokens sessions,
    IHttpClientFactory httpClients,
    StudioOptions studio,
    ILogger<AuthorTokenExchange> logger) : IAuthorTokenExchange
{
    public async Task<AuthorTokenExchangeResult> ExchangeAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var tenantId = httpContext.RequestServices.GetRequiredService<ITenantContextAccessor>().Current?.TenantId;
        if (tenantId is null
            || settings.Find(tenantId) is not { } tenant
            || tenant.Profile == TenantSignInProfiles.Fapi2
            || catalog.Find(tenantId)?.RealmAgent is not { } agent)
        {
            return Refuse(tenantId, AuthorTokenOutcome.NotOffered);
        }

        var refreshed = await sessions.RefreshAsync(httpContext, cancellationToken);
        if (refreshed.Unavailable)
        {
            return Refuse(tenantId, AuthorTokenOutcome.Unavailable);
        }

        if (refreshed.AccessToken is not { } platformToken)
        {
            return Refuse(tenantId, AuthorTokenOutcome.Refused);
        }

        RealmAgentExchange exchanged;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(SessionTokens.RefreshLimit);
            exchanged = await new RealmAgentTokens(httpClients.CreateClient(), agent).ExchangeAsync(platformToken, limit.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            return Refuse(tenantId, AuthorTokenOutcome.Unavailable);
        }

        if (exchanged.IsRefused)
        {
            return Refuse(tenantId, AuthorTokenOutcome.Refused);
        }

        var roles = new JsonWebToken(exchanged.AccessToken).Claims
            .Where(claim => claim.Type == tenant.Claims.Roles)
            .Select(claim => claim.Value)
            .ToList();
        return studio.ForTenant(tenantId).Allows(roles)
            ? new AuthorTokenExchangeResult(AuthorTokenOutcome.Exchanged, exchanged.RefreshToken)
            : Refuse(tenantId, AuthorTokenOutcome.NotAuthor);
    }

    private AuthorTokenExchangeResult Refuse(string? tenantId, AuthorTokenOutcome outcome)
    {
        LogRefused(logger, tenantId ?? "none", outcome);
        return new AuthorTokenExchangeResult(outcome);
    }

    [LoggerMessage(LogLevel.Information, "The token exchange for the agent host of tenant {TenantId} did not give a token: {Outcome}.")]
    private static partial void LogRefused(ILogger logger, string tenantId, AuthorTokenOutcome outcome);
}
