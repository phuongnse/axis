namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.package_runners</c>: a customer-hosted package runner of the tenant, with the client id of its client in the
/// tenant's realm, see <c>docs/reference/user-administration.md#package-runners</c>. The row never holds the client's secret.
/// </summary>
public sealed class PackageRunnerRow
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The client id of the runner's client in the tenant's realm. It is unique.</summary>
    public required string ClientId { get; init; }

    /// <summary>The packages the runner registered, as a JSON array of <c>{ "id", "version" }</c>. It is empty until a runner registers.</summary>
    public string Packages { get; init; } = "[]";

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The time of the runner's last take of a call. It is null before the first.</summary>
    public DateTimeOffset? LastCallAt { get; init; }

    /// <summary>The time the runner was revoked. It is null while the runner is not revoked.</summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
