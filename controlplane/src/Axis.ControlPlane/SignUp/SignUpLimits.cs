using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading.RateLimiting;
using Axis.Core.Telemetry;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The <c>sign_up</c> rate limit of <c>POST /api/sign-up</c>: 5 tries an hour from one client address and 20 tries a day for one
/// email domain, except the <c>ControlPlane:SignUp:PublicMailboxDomains</c>, whose addresses count one by one, see
/// <c>docs/reference/sign-up.md#bots-and-abuse</c>. As the platform's limits are by decision D85, each replica counts in memory, and a
/// restart starts the counts again. A refusal counts in <c>axis.rate_limits.refused</c>, with the partition <c>sign_up</c> and never
/// an address or a domain. The meter belongs to the host's meter factory.
/// </summary>
internal sealed class SignUpLimits : IDisposable
{
    private const string Partition = "sign_up";

    private readonly PartitionedRateLimiter<string> _addresses = Limiter(permitLimit: 5, TimeSpan.FromHours(1));
    private readonly PartitionedRateLimiter<string> _domains = Limiter(permitLimit: 20, TimeSpan.FromDays(1));
    private readonly Counter<long> _refused;
    private readonly HashSet<string> _publicMailboxDomains;

    public SignUpLimits(ControlPlaneOptions options, IMeterFactory meterFactory)
    {
        _publicMailboxDomains = new HashSet<string>(options.SignUp.PublicMailboxDomains, StringComparer.OrdinalIgnoreCase);
        var meter = meterFactory.Create(AxisMetrics.MeterName);
        _refused = meter.CreateCounter<long>(AxisMetrics.RateLimitRefusals, "{request}", "Requests refused by a rate limit.");
    }

    /// <summary>Counts a try from the client address of the request. The answer is the <c>429</c> past the limit, or null.</summary>
    public IResult? RefuseAddress(HttpContext context) =>
        Refuse(_addresses, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", context, "address");

    /// <summary>
    /// Counts a try for the domain of <paramref name="email"/>, a valid address. At a public mailbox domain the count is of the
    /// address in lower case without its <c>+tag</c>, as <c>one trial for each email</c> compares addresses. The answer is the
    /// <c>429</c> past the limit, or null.
    /// </summary>
    public IResult? RefuseDomain(HttpContext context, string email)
    {
        var at = email.LastIndexOf('@');
        var domain = email[(at + 1)..].ToLowerInvariant();
        var key = domain;
        if (_publicMailboxDomains.Contains(domain))
        {
            var local = email[..at];
            var tag = local.IndexOf('+', StringComparison.Ordinal);
            key = $"{(tag < 0 ? local : local[..tag]).ToLowerInvariant()}@{domain}";
        }

        return Refuse(_domains, key, context, "domain");
    }

    public void Dispose()
    {
        _addresses.Dispose();
        _domains.Dispose();
    }

    // Both limiters replenish from the partitioned limiter's own heartbeat, so no partition needs a timer.
    private static PartitionedRateLimiter<string> Limiter(int permitLimit, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = false,
        }));

    private IResult? Refuse(PartitionedRateLimiter<string> limiter, string key, HttpContext context, string limit)
    {
        using var lease = limiter.AttemptAcquire(key);
        if (lease.IsAcquired)
        {
            return null;
        }

        // The limiter reports the window, not the time left in it, so the wait is never understated.
        var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? (long)Math.Ceiling(retryAfter.TotalSeconds) : 1;
        context.Response.Headers.RetryAfter = Math.Max(1, seconds).ToString(CultureInfo.InvariantCulture);
        _refused.Add(1, new KeyValuePair<string, object?>(AxisTelemetry.PartitionTag, Partition));
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many requests.",
            extensions: new Dictionary<string, object?> { ["limit"] = limit });
    }
}
