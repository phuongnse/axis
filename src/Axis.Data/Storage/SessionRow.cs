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

    /// <summary>
    /// The time of the person's last login at the identity provider, from the ID token's <c>auth_time</c>. It is null for a
    /// session created before the column existed.
    /// </summary>
    public DateTimeOffset? AuthenticatedAt { get; init; }

    /// <summary>
    /// The lower-case site path of the host that made the session. It is null for the tenant's own hosts and for a session
    /// created before the column existed.
    /// </summary>
    public string? SitePath { get; init; }

    /// <summary>
    /// The alias of the identity provider that signed the person in, from the ID token's <c>identity_provider</c> claim. It is null
    /// for a sign-in through the tenant's accounts and for a session created before the column existed.
    /// </summary>
    public string? IdentityProvider { get; init; }

    /// <summary>
    /// The roles that the identity provider's <c>roles</c> mapping gave each application at sign-in, as a JSON object from the
    /// application id to the list of role names. It is <c>{}</c> for a sign-in through the tenant's accounts and for a session
    /// created before the column existed.
    /// </summary>
    public string ApplicationRoles { get; init; } = "{}";
}
