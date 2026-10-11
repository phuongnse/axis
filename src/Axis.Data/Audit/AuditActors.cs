namespace Axis.Data.Audit;

/// <summary>The actors of audit records that are not a user id.</summary>
public static class AuditActors
{
    /// <summary>
    /// Nobody was signed in. Only process starts from before they needed a session wrote it, so
    /// it appears only in older audit records.
    /// </summary>
    public const string Anonymous = "anonymous";

    /// <summary>The worker, for the steps it runs.</summary>
    public const string System = "system";
}
