using System.Globalization;
using System.Threading.RateLimiting;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Telemetry;
using Microsoft.AspNetCore.RateLimiting;

namespace Axis.Server.Http;

/// <summary>The rate limit partitions. Each has its own setting and its own counts.</summary>
internal enum Partition
{
    SignIn,
    Session,
    Bearer,
    Publishing,
}

/// <summary>
/// How a route is limited, as endpoint metadata. <see cref="Session"/> is the partition for a caller with a session, or
/// null when the route takes no session. <see cref="Bearer"/> is whether the route takes a bearer token, so a bearer
/// caller counts as bearer. <see cref="Open"/> is a route with no limit.
/// </summary>
internal sealed record RateLimitRoute(Partition? Session, bool Bearer, bool Open = false);

/// <summary>
/// The rate limits of D85. Each replica counts on its own, in memory. The partition follows the route and the kind of
/// caller, and the limiter runs after the tenant, the CSRF check and authentication, so a refused write costs nothing.
/// </summary>
internal static class RateLimits
{
    private const string PartitionKey = "Axis.RateLimits.Partition";

    private const string UnknownAddress = "unknown";

    /// <summary>Whether the request is the sign-in callback, which the OIDC handler answers inside authentication.</summary>
    public static bool IsCallback(HttpContext context) =>
        context.Request.Path.Equals(TenantSignIn.CallbackPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Counts a session caller of the route in <paramref name="session"/>.</summary>
    public static TBuilder WithRateLimit<TBuilder>(this TBuilder builder, Partition session)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitRoute(session, Bearer: false));

    /// <summary>Counts a caller of a route that takes only a bearer token in the bearer partition.</summary>
    public static TBuilder WithBearerRateLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitRoute(Session: null, Bearer: true));

    /// <summary>Gives the route no limit.</summary>
    public static TBuilder WithoutRateLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitRoute(Session: null, Bearer: false, Open: true));

    /// <summary>
    /// Picks the partition and the count key of a request, or null when the request has no limit. The callback has no
    /// route and no caller yet, so it counts with sign-in by client address. A caller with a bearer token counts as
    /// bearer, a session caller by the partition of the route, and a request with neither by tenant and client address.
    /// </summary>
    public static (Partition Partition, string Key)? Choose(HttpContext context, string? tenantId)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;
        if (IsCallback(context))
        {
            return (Partition.SignIn, address);
        }

        if (context.GetEndpoint()?.Metadata.GetMetadata<RateLimitRoute>() is not { Open: false } route)
        {
            return null;
        }

        var tenant = tenantId ?? string.Empty;
        if (route.Bearer && EndpointCaller.Read(context.User) is { } caller)
        {
            return (Partition.Bearer, $"{tenant}|{caller.Id}");
        }

        if (route.Session == Partition.SignIn)
        {
            return (Partition.SignIn, address);
        }

        if (route.Session is { } session && Session.Read(context.User) is { } user)
        {
            return (session, $"{tenant}|{user.Id}");
        }

        return (route.Session ?? Partition.Bearer, $"{tenant}|addr:{address}");
    }

    /// <summary>Sets the limiter, its refusal answer and the refusal count.</summary>
    public static void Configure(RateLimiterOptions options, RateLimitSettings settings, RateLimitRefusals refusals)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(refusals);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, (Partition, string)>(context =>
        {
            var tenantId = context.RequestServices.GetService<ITenantContextAccessor>()?.Current?.TenantId;
            if (Choose(context, tenantId) is not { } chosen)
            {
                return RateLimitPartition.GetNoLimiter<(Partition, string)>(default);
            }

            var (partition, key) = chosen;
            context.Items[PartitionKey] = partition;
            var limit = settings.For(partition);
            return RateLimitPartition.GetTokenBucketLimiter((partition, key), _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limit,
                TokensPerPeriod = Math.Max(1, (int)Math.Ceiling(limit / 6.0)),
                ReplenishmentPeriod = settings.ReplenishmentPeriod,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
        options.OnRejected = async (rejected, _) =>
        {
            var context = rejected.HttpContext;
            var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : settings.ReplenishmentPeriod;
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            if (context.Items[PartitionKey] is Partition partition)
            {
                refusals.Record(partition);
            }

            await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Too many requests.").ExecuteAsync(context);
        };
    }
}
