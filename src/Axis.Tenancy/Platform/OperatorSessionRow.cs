namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.operator_sessions</c>: the session of a platform operator who signed in to the cell console. It
/// belongs to no tenant. The key is the SHA-256 hash of the random id that the operator's cookie holds, so the row never
/// holds the id or a token.
/// </summary>
public sealed class OperatorSessionRow
{
    public byte[] IdHash { get; set; } = [];

    /// <summary>The <c>sub</c> of the operator in the operator realm.</summary>
    public string OperatorId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>The realm roles of the access token at sign-in.</summary>
    public string[] Roles { get; set; } = [];

    public DateTimeOffset SignedInAt { get; set; }

    public DateTimeOffset LastRequestAt { get; set; }
}
