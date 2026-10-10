namespace Axis.Data.Audit;

/// <summary>The actors of audit records that are not a user id.</summary>
public static class AuditActors
{
    /// <summary>Nobody is signed in. Record writes stay open this way until M4.</summary>
    public const string Anonymous = "anonymous";
}
