namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_host_requests</c>: one request of a tenant for a host of its own, such as
/// <c>approvals.acme.vn</c>. A request is <c>pending</c> until a later check of the customer's DNS records marks it
/// <c>verified</c> or <c>expired</c>. A pending request does not hold the host, only <c>platform.tenant_hosts</c> does. Each
/// request is a new row, so the history stays, and a row goes with its tenant.
/// </summary>
public sealed class TenantHostRequestRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The requested host in lower case.</summary>
    public string Host { get; set; } = "";

    /// <summary>The verification token, 32 random bytes as base64url, that the customer puts in the <c>TXT</c> record.</summary>
    public string Token { get; set; } = "";

    public string State { get; set; } = "pending";

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the last DNS check ran, or null when none has.</summary>
    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>Who asked for the host, such as an operator.</summary>
    public string Actor { get; set; } = "";
}
