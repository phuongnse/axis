namespace Axis.Configuration.Storage;

/// <summary>
/// A request to validate, publish, roll back or test an application, stored by the server and run by the
/// publisher. The server only inserts a row. The publisher changes its status and lease.
/// </summary>
public sealed class PublishRequest
{
    public required Guid Id { get; init; }

    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary><c>validate</c>, <c>publish</c>, <c>rollback</c> or <c>test</c>.</summary>
    public required string Kind { get; init; }

    /// <summary><c>queued</c>, <c>running</c>, <c>succeeded</c> or <c>failed</c>.</summary>
    public required string Status { get; init; }

    /// <summary><c>tenant</c> runs the request on the tenant itself, <c>preview</c> runs a publish on the tenant's preview tenant.</summary>
    public string Target { get; init; } = "tenant";

    /// <summary>The resource files as a JSON array, or null for a rollback.</summary>
    public string? Snapshot { get; init; }

    public Guid? ReleaseId { get; init; }

    public string? Label { get; init; }

    public string? Note { get; init; }

    public required string RequestedBy { get; init; }

    public string? RequestedByName { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public string? ClaimedBy { get; init; }

    public DateTimeOffset? LeaseUntil { get; init; }

    public int Claims { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>The diagnostics as a JSON array.</summary>
    public string? Diagnostics { get; init; }

    /// <summary>For a <c>test</c>, the scenario names to run as a JSON array. Null runs every scenario.</summary>
    public string? ScenarioNames { get; init; }

    /// <summary>For a <c>test</c>, the result of each scenario as a JSON array. Null until the request ends.</summary>
    public string? ScenarioResults { get; init; }
}
