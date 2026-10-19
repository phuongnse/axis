namespace Axis.Tenancy.Platform;

/// <summary>A row of <c>platform.tenant_bindings</c>. A name is unique per tenant ignoring letter case.</summary>
public sealed class TenantBindingRow
{
    public string TenantId { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary><c>url</c> or <c>secret</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The URL of a <c>url</c> binding.</summary>
    public string? Url { get; set; }

    /// <summary>The encrypted value of a <c>secret</c> binding.</summary>
    public byte[]? SecretValue { get; set; }
}
