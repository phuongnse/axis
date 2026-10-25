using System.Globalization;

namespace Axis.Server.Http;

/// <summary>
/// The requests per minute that one replica allows in each rate limit partition, from <c>RateLimits:SignIn</c>,
/// <c>RateLimits:Session</c>, <c>RateLimits:Bearer</c> and <c>RateLimits:Publishing</c>. A missing setting takes its default.
/// </summary>
internal sealed record RateLimitSettings(int SignIn, int Session, int Bearer, int Publishing)
{
    public const string SignInSetting = "RateLimits:SignIn";

    public const string SessionSetting = "RateLimits:Session";

    public const string BearerSetting = "RateLimits:Bearer";

    public const string PublishingSetting = "RateLimits:Publishing";

    public const int DefaultSignIn = 300;

    public const int DefaultSession = 1200;

    public const int DefaultBearer = 1200;

    public const int DefaultPublishing = 60;

    /// <summary>How often a caller's allowance is topped up, by a sixth of the setting. Tests make it long.</summary>
    public TimeSpan ReplenishmentPeriod { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The requests per minute of <paramref name="partition"/>.</summary>
    public int For(Partition partition) => partition switch
    {
        Partition.SignIn => SignIn,
        Partition.Session => Session,
        Partition.Bearer => Bearer,
        Partition.Publishing => Publishing,
        _ => throw new ArgumentOutOfRangeException(nameof(partition)),
    };

    /// <exception cref="InvalidOperationException">A setting is not a whole number, or is zero or less.</exception>
    public static RateLimitSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new RateLimitSettings(
            Read(configuration, SignInSetting, DefaultSignIn),
            Read(configuration, SessionSetting, DefaultSession),
            Read(configuration, BearerSetting, DefaultBearer),
            Read(configuration, PublishingSetting, DefaultPublishing));
    }

    private static int Read(IConfiguration configuration, string setting, int fallback)
    {
        if (configuration[setting] is not { } text)
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidOperationException($"{setting} is not a whole number: '{text}'.");
        }

        return value > 0 ? value : throw new InvalidOperationException($"{setting} must be greater than zero.");
    }
}
