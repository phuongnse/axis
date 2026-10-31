namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_data_keys</c>: one numbered key of a tenant. <see cref="Key"/> holds the base64 text of 32 random
/// bytes encrypted by <see cref="TenantSecretCipher"/> with the field <c>data_key:&lt;n&gt;</c> or <c>lookup_key:&lt;n&gt;</c>,
/// never the plain key.
/// </summary>
public sealed class TenantDataKeyRow
{
    public string TenantId { get; set; } = "";

    /// <summary>One of <see cref="TenantKeyKinds"/>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The key's number, from 1. A tenant's newest key of a kind has the highest number.</summary>
    public int Number { get; set; }

    public byte[] Key { get; set; } = [];
}

/// <summary>The kinds of tenant key that <c>platform.tenant_data_keys</c> stores.</summary>
public static class TenantKeyKinds
{
    public const string Data = "data";

    public const string Lookup = "lookup";
}
