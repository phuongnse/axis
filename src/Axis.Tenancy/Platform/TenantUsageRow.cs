namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_usage</c>: the usage of one tenant on one UTC day. It holds counts and sizes only, so
/// it never holds a record value, a file name or a user name.
/// </summary>
public sealed class TenantUsageRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The UTC day.</summary>
    public DateOnly Day { get; set; }

    /// <summary>The database bytes of the day's largest storage sample.</summary>
    public long DatabaseBytes { get; set; }

    /// <summary>The file bytes of the day's largest storage sample.</summary>
    public long FileBytes { get; set; }

    /// <summary>The users at the end of the day, or <c>null</c> while nothing counts them.</summary>
    public int? Users { get; set; }

    /// <summary>The applications at the end of the day, or <c>null</c> while nothing counts them.</summary>
    public int? Applications { get; set; }

    public int EmailsSent { get; set; }

    /// <summary>The connector calls and package calls attempted on the day.</summary>
    public long ConnectorCalls { get; set; }

    public long WorkItemsRun { get; set; }
}
