namespace Axis.Tenancy;

/// <summary>The keys of the quotas of a tenant in a cell, see the quotas table in <c>docs/reference/tenant-quotas.md#quotas</c>.</summary>
public static class QuotaKeys
{
    public const string Storage = "storage";

    public const string Users = "users";

    public const string Applications = "applications";

    public const string EmailsPerDay = "emailsPerDay";

    public const string ConcurrentImports = "concurrentImports";

    public const string ConcurrentConnectorCalls = "concurrentConnectorCalls";

    public const string ConcurrentQueries = "concurrentQueries";

    public const string WorkerWeight = "workerWeight";

    public const string AuthoringRequests = "authoringRequests";

    /// <summary>Every quota key, in the order of the quotas table.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Storage,
        Users,
        Applications,
        EmailsPerDay,
        ConcurrentImports,
        ConcurrentConnectorCalls,
        ConcurrentQueries,
        WorkerWeight,
        AuthoringRequests,
    ];
}
