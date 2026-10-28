using System.Globalization;
using Axis.Data.Storage;

namespace Axis.Server.Authentication;

/// <summary>
/// The rule that the callback applies to the ID token's <c>auth_time</c>, the time the person last logged in at the
/// identity provider. A login older than the session's absolute lifetime would let a person start a new session
/// without logging in again, so the callback refuses it.
/// </summary>
internal static class SignInRecency
{
    /// <summary>
    /// Whether <paramref name="authTime"/>, a NumericDate in seconds, is no older than the session's absolute lifetime at
    /// <paramref name="now"/>. A missing or unreadable value is not recent.
    /// </summary>
    /// <param name="clockSkew">The slack for a clock difference between Axis and the identity provider.</param>
    public static bool IsRecent(string? authTime, DateTimeOffset now, TimeSpan clockSkew)
    {
        if (string.IsNullOrWhiteSpace(authTime)
            || !long.TryParse(authTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || seconds < DateTimeOffset.MinValue.ToUnixTimeSeconds()
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        return now - DateTimeOffset.FromUnixTimeSeconds(seconds) <= SessionLifetime.MaxLifetime + clockSkew;
    }
}
