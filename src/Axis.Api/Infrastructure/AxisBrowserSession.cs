using System.Globalization;
using System.Security.Cryptography;
using Axis.Api.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using StackExchange.Redis;

namespace Axis.Api.Infrastructure;

internal sealed record AxisBrowserSessionPolicy(TimeSpan IdleLifetime, TimeSpan AbsoluteLifetime)
{
    public const string AbsoluteExpiresAtProperty = "axis:session:absolute-expires-at";

    public static AxisBrowserSessionPolicy Load(IConfiguration configuration)
    {
        int idleMinutes = configuration.GetValue("BrowserSession:IdleMinutes", 30);
        int absoluteHours = configuration.GetValue("BrowserSession:AbsoluteHours", 8);
        if (idleMinutes <= 0)
            throw new InvalidOperationException("BrowserSession:IdleMinutes must be greater than zero.");
        if (absoluteHours <= 0)
            throw new InvalidOperationException("BrowserSession:AbsoluteHours must be greater than zero.");

        TimeSpan idle = TimeSpan.FromMinutes(idleMinutes);
        TimeSpan absolute = TimeSpan.FromHours(absoluteHours);
        if (idle > absolute)
            throw new InvalidOperationException("BrowserSession:IdleMinutes cannot exceed BrowserSession:AbsoluteHours.");
        return new AxisBrowserSessionPolicy(idle, absolute);
    }

    public AuthenticationProperties CreateAuthenticationProperties()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset absoluteExpiresAt = now.Add(AbsoluteLifetime);
        return new AuthenticationProperties
        {
            AllowRefresh = true,
            ExpiresUtc = now.Add(IdleLifetime),
            IsPersistent = false,
            IssuedUtc = now,
            Items =
            {
                [AbsoluteExpiresAtProperty] = absoluteExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            },
        };
    }

    public static bool IsPastAbsoluteExpiry(AuthenticationProperties properties, DateTimeOffset now) =>
        !properties.Items.TryGetValue(AbsoluteExpiresAtProperty, out string? raw) ||
        !DateTimeOffset.TryParseExact(
            raw,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset absoluteExpiresAt) ||
        absoluteExpiresAt <= now;
}

internal interface IWorkspaceTransitionTicketCleanup
{
    Task RemoveByCorrelationDigestAsync(
        string correlationDigest,
        bool transition,
        CancellationToken cancellationToken);
}

internal sealed class RedisTicketStore(
    IConnectionMultiplexer redis,
    IDataProtectionProvider dataProtectionProvider)
    : ITicketStore, IWorkspaceTransitionTicketCleanup
{
    private const string KeyPrefix = "axis:browser-session:";
    private const string SessionCorrelationIndexPrefix = "axis:browser-session-correlation:";
    private const string TransitionCorrelationIndexPrefix = "axis:workspace-transition-correlation:";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "Axis.Api",
        "BrowserSessionTicket",
        "v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        IDatabase database = redis.GetDatabase();
        byte[] payload = Protect(ticket);
        TimeSpan lifetime = RemainingLifetime(ticket);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string key = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(
                RandomNumberGenerator.GetBytes(32));
            bool stored = await database.StringSetAsync(
                    KeyPrefix + key,
                    payload,
                    lifetime,
                    When.NotExists)
                .WaitAsync(cancellationToken);
            if (stored)
            {
                return await StoreCorrelationIndexAsync(
                    database,
                    ticket,
                    key,
                    lifetime,
                    cancellationToken);
            }
        }

        throw new InvalidOperationException("Could not allocate a unique browser session identifier.");
    }

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext context,
        CancellationToken cancellationToken) =>
        StoreAsync(ticket, cancellationToken);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        bool renewed = await redis.GetDatabase().StringSetAsync(
                KeyPrefix + key,
                Protect(ticket),
                RemainingLifetime(ticket),
                When.Exists)
            .WaitAsync(cancellationToken);
        if (!renewed)
            throw new InvalidOperationException("The browser session no longer exists.");

        await StoreCorrelationIndexAsync(
            redis.GetDatabase(),
            ticket,
            key,
            RemainingLifetime(ticket),
            cancellationToken,
            replace: true);
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext context,
        CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, cancellationToken);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        RedisValue value = await redis.GetDatabase().StringGetAsync(KeyPrefix + key)
            .WaitAsync(cancellationToken);
        if (value.IsNull)
            return null;

        try
        {
            return TicketSerializer.Default.Deserialize(protector.Unprotect((byte[])value!));
        }
        catch (CryptographicException)
        {
            await RemoveAsync(key, cancellationToken);
            return null;
        }
    }

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext context,
        CancellationToken cancellationToken) =>
        RetrieveAsync(key, cancellationToken);

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        IDatabase database = redis.GetDatabase();
        RedisValue value = await database.StringGetAsync(KeyPrefix + key)
            .WaitAsync(cancellationToken);
        string? indexKey = CorrelationIndexKey(value);
        RedisKey[] keys = indexKey is null
            ? [KeyPrefix + key]
            : [KeyPrefix + key, indexKey];
        await database.KeyDeleteAsync(keys).WaitAsync(cancellationToken);
    }

    public Task RemoveAsync(
        string key,
        HttpContext context,
        CancellationToken cancellationToken) =>
        RemoveAsync(key, cancellationToken);

    public async Task RemoveByCorrelationDigestAsync(
        string correlationDigest,
        bool transition,
        CancellationToken cancellationToken)
    {
        string indexKey = (transition
            ? TransitionCorrelationIndexPrefix
            : SessionCorrelationIndexPrefix) + correlationDigest;
        IDatabase database = redis.GetDatabase();
        RedisValue ticketKey = await database.StringGetAsync(indexKey)
            .WaitAsync(cancellationToken);
        RedisKey[] keys = ticketKey.IsNull
            ? [indexKey]
            : [indexKey, KeyPrefix + ticketKey.ToString()];
        await database.KeyDeleteAsync(keys).WaitAsync(cancellationToken);
    }

    private byte[] Protect(AuthenticationTicket ticket) =>
        protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private async Task<string> StoreCorrelationIndexAsync(
        IDatabase database,
        AuthenticationTicket ticket,
        string ticketKey,
        TimeSpan lifetime,
        CancellationToken cancellationToken,
        bool replace = false)
    {
        string? indexKey = CorrelationIndexKey(ticket);
        if (indexKey is null)
            return ticketKey;

        bool stored = await database.StringSetAsync(
                indexKey,
                ticketKey,
                lifetime,
                replace ? When.Always : When.NotExists)
            .WaitAsync(cancellationToken);
        if (stored)
            return ticketKey;

        RedisValue existingTicketKey = await database.StringGetAsync(indexKey)
            .WaitAsync(cancellationToken);
        RedisValue existingPayload = existingTicketKey.IsNull
            ? RedisValue.Null
            : await database.StringGetAsync(KeyPrefix + existingTicketKey.ToString())
                .WaitAsync(cancellationToken);
        AuthenticationTicket? existingTicket = TryUnprotect(existingPayload);
        if (existingTicket is not null
            && HasSameIdentity(ticket, existingTicket))
        {
            await database.KeyDeleteAsync(KeyPrefix + ticketKey).WaitAsync(cancellationToken);
            return existingTicketKey.ToString();
        }

        await database.KeyDeleteAsync(KeyPrefix + ticketKey).WaitAsync(cancellationToken);
        throw new InvalidOperationException("The browser session correlation already exists.");
    }

    private AuthenticationTicket? TryUnprotect(RedisValue payload)
    {
        if (payload.IsNull)
            return null;

        try
        {
            return TicketSerializer.Default.Deserialize(protector.Unprotect((byte[])payload!));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool HasSameIdentity(
        AuthenticationTicket expected,
        AuthenticationTicket actual) =>
        StringComparer.Ordinal.Equals(expected.AuthenticationScheme, actual.AuthenticationScheme)
        && expected.Principal.Claims
            .Select(claim => (claim.Type, claim.Value, claim.ValueType, claim.Issuer))
            .OrderBy(claim => claim.Type, StringComparer.Ordinal)
            .ThenBy(claim => claim.Value, StringComparer.Ordinal)
            .ThenBy(claim => claim.ValueType, StringComparer.Ordinal)
            .ThenBy(claim => claim.Issuer, StringComparer.Ordinal)
            .SequenceEqual(actual.Principal.Claims
                .Select(claim => (claim.Type, claim.Value, claim.ValueType, claim.Issuer))
                .OrderBy(claim => claim.Type, StringComparer.Ordinal)
                .ThenBy(claim => claim.Value, StringComparer.Ordinal)
                .ThenBy(claim => claim.ValueType, StringComparer.Ordinal)
                .ThenBy(claim => claim.Issuer, StringComparer.Ordinal));

    private string? CorrelationIndexKey(RedisValue protectedTicket)
    {
        if (protectedTicket.IsNull)
            return null;

        try
        {
            return CorrelationIndexKey(
                TicketSerializer.Default.Deserialize(
                    protector.Unprotect((byte[])protectedTicket!)));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string? CorrelationIndexKey(AuthenticationTicket? ticket)
    {
        if (ticket is null)
            return null;

        string? correlation;
        string prefix;
        if (StringComparer.Ordinal.Equals(
                ticket.AuthenticationScheme,
                AxisApiServiceExtensions.WorkspaceTransitionScheme))
        {
            correlation = ticket.Principal.FindFirst(
                WorkspaceTransitionTicket.TargetCorrelationClaim)?.Value;
            prefix = TransitionCorrelationIndexPrefix;
        }
        else
        {
            correlation = ticket.Principal.FindFirst(BrowserSessionCorrelation.ClaimType)?.Value;
            prefix = SessionCorrelationIndexPrefix;
        }

        return string.IsNullOrWhiteSpace(correlation)
            ? null
            : prefix + BrowserSessionCorrelation.Digest(correlation);
    }

    private static TimeSpan RemainingLifetime(AuthenticationTicket ticket)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = ticket.Properties.ExpiresUtc;
        if (ticket.Properties.Items.TryGetValue(
                AxisBrowserSessionPolicy.AbsoluteExpiresAtProperty,
                out string? rawAbsolute) &&
            DateTimeOffset.TryParseExact(
                rawAbsolute,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset absoluteExpiresAt))
        {
            expiresAt = expiresAt is null || absoluteExpiresAt < expiresAt
                ? absoluteExpiresAt
                : expiresAt;
        }

        if (expiresAt is null || expiresAt <= now)
            throw new InvalidOperationException("The browser session ticket has no valid remaining lifetime.");
        return expiresAt.Value - now;
    }
}
