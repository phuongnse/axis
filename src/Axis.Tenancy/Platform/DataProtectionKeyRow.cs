namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.data_protection_keys</c>: one ASP.NET data protection key. <see cref="Xml"/> holds the key
/// XML encrypted by <see cref="TenantSecretCipher"/>, never the plain key.
/// </summary>
public sealed class DataProtectionKeyRow
{
    /// <summary>The key's own id, from the <c>id</c> attribute of its XML.</summary>
    public Guid Id { get; set; }

    public string FriendlyName { get; set; } = "";

    public byte[] Xml { get; set; } = [];
}
