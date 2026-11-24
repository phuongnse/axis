namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.host_keys</c>: the public half of the key pair of one host, such as the agent host, with its key id and the time it
/// was published. It holds a public key only, never a private key. The table has no foreign key.
/// </summary>
public sealed class HostKeyRow
{
    /// <summary>The host the key belongs to, such as <see cref="HostKeyStore.AgentHost"/>.</summary>
    public string Host { get; set; } = "";

    /// <summary>The RFC 7638 thumbprint of the public key.</summary>
    public string KeyId { get; set; } = "";

    /// <summary>The uncompressed EC P-256 point, 65 bytes.</summary>
    public byte[] PublicKey { get; set; } = [];

    public DateTimeOffset PublishedAt { get; set; }
}
