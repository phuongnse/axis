namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.sessions</c>: the server-side state of one signed-in session. The row never
/// holds the session id or a token, only a hash of the id. The cookie holds the id.
/// </summary>
public sealed class SessionRow
{
    /// <summary>The SHA-256 hash of the session id, which is 32 random bytes.</summary>
    public required byte[] IdHash { get; init; }

    public required string UserId { get; init; }

    public required string DisplayName { get; init; }

    public required string[] Roles { get; init; }

    public required DateTimeOffset SignedInAt { get; init; }

    /// <summary>The time of the last request that carried the session.</summary>
    public required DateTimeOffset LastRequestAt { get; init; }
}
