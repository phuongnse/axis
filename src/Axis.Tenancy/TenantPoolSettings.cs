using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The size of the connection pool of each tenant database, for the runtime role in the server and the worker. The
/// settings win over the pool keywords of a tenant's connection string, and automatic statement preparation is always off.
/// </summary>
public sealed class TenantPoolSettings
{
    public const string MaxSizeSetting = "Data:TenantPoolMaxSize";
    public const string MinSizeSetting = "Data:TenantPoolMinSize";
    public const string IdleLifetimeSetting = "Data:TenantPoolIdleLifetime";

    /// <summary>5 connections at most, none kept open and an idle lifetime of 60 seconds.</summary>
    public static TenantPoolSettings Default { get; } = new(5, 0, TimeSpan.FromSeconds(60));

    /// <exception cref="InvalidOperationException">A value is out of range. The message names the setting.</exception>
    public TenantPoolSettings(int maxSize, int minSize, TimeSpan idleLifetime)
    {
        if (maxSize < 1)
        {
            throw new InvalidOperationException($"'{MaxSizeSetting}' must be an integer of at least 1.");
        }

        if (minSize < 0 || minSize > maxSize)
        {
            throw new InvalidOperationException($"'{MinSizeSetting}' must be an integer from 0 up to '{MaxSizeSetting}'.");
        }

        if (idleLifetime < TimeSpan.FromSeconds(1)
            || idleLifetime.Ticks % TimeSpan.TicksPerSecond != 0
            || idleLifetime.TotalSeconds > int.MaxValue)
        {
            throw new InvalidOperationException($"'{IdleLifetimeSetting}' must be a duration of whole seconds, at least 00:00:01.");
        }

        MaxSize = maxSize;
        MinSize = minSize;
        IdleLifetime = idleLifetime;
    }

    public int MaxSize { get; }

    public int MinSize { get; }

    public TimeSpan IdleLifetime { get; }

    /// <summary>
    /// Reads the three settings. A missing or empty value takes its default.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is invalid. The message names the setting.</exception>
    public static TenantPoolSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var maxSize = ReadInteger(configuration, MaxSizeSetting, Default.MaxSize, "an integer of at least 1");
        var minSize = ReadInteger(configuration, MinSizeSetting, Default.MinSize, "an integer from 0 up to '" + MaxSizeSetting + "'");
        var idleLifetime = Default.IdleLifetime;
        if (configuration[IdleLifetimeSetting] is { Length: > 0 } text)
        {
            idleLifetime = TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"'{IdleLifetimeSetting}' must be a duration of whole seconds, at least 00:00:01.");
        }

        return new TenantPoolSettings(maxSize, minSize, idleLifetime);
    }

    /// <summary>
    /// Returns <paramref name="connectionString"/> with the pool keywords of these settings and
    /// <c>Max Auto Prepare</c> 0. The pruning interval drops to the idle lifetime when it is longer, because Npgsql
    /// refuses a pool that prunes less often than connections idle out.
    /// </summary>
    public string Apply(string connectionString)
    {
        var idleSeconds = (int)IdleLifetime.TotalSeconds;
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = MaxSize,
            MinPoolSize = MinSize,
            ConnectionIdleLifetime = idleSeconds,
            MaxAutoPrepare = 0,
        };
        builder.ConnectionPruningInterval = Math.Min(builder.ConnectionPruningInterval, idleSeconds);
        return builder.ConnectionString;
    }

    private static int ReadInteger(IConfiguration configuration, string setting, int fallback, string expected)
    {
        if (configuration[setting] is not { Length: > 0 } text)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"'{setting}' must be {expected}.");
    }
}
