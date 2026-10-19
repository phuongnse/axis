namespace Axis.Data.Audit;

/// <summary>The actors of audit records that are not a user id.</summary>
public static class AuditActors
{
    /// <summary>
    /// Nobody was signed in. A <c>session.signInRefused</c> record has it, and so do older
    /// process starts from before they needed a session.
    /// </summary>
    public const string Anonymous = "anonymous";

    /// <summary>The worker, for the steps it runs.</summary>
    public const string System = "system";
}
