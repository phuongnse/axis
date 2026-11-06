using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Queues the <c>writeTenantRecord</c> and <c>removeTenantRecord</c> jobs, see <c>docs/reference/region-directory.md#routing-a-host</c>.
/// The payloads hold ids and the tenant domain, never the record's target: a job reads the tenant's row in the directory when it
/// runs, so a late job can only make the record match the directory. The jobs live in the control plane database and the directory
/// in its own, so a caller queues them after the directory transaction commits.
/// </summary>
internal static class TenantRecordJobs
{
    public const string WriteKind = "writeTenantRecord";
    public const string RemoveKind = "removeTenantRecord";

    /// <summary>The name of a tenant's record, such as <c>acme.vn.example.com</c>.</summary>
    public static string Name(string tenantId, string tenantDomain) => $"{tenantId}.{tenantDomain}".ToLowerInvariant();

    /// <summary>The ingress of a cell, which the record of each of its tenants points to.</summary>
    public static string Target(string cellId, string tenantDomain) => $"ingress.{cellId}.{tenantDomain}";

    /// <summary>Queues a job that writes the record of <paramref name="tenantId"/>. The payload is <c>{ tenantId }</c>.</summary>
    public static async Task EnqueueWriteAsync(NpgsqlDataSource controlPlane, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controlPlane);
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await JobQueue.EnqueueAsync(connection, null, WriteKind, new JsonObject { ["tenantId"] = tenantId }, null, cancellationToken);
    }

    /// <summary>
    /// Queues a job that removes the record of <paramref name="tenantId"/>. The payload is <c>{ tenantId, tenantDomain }</c>, as the
    /// job cannot look up the domain of a tenant whose row is gone.
    /// </summary>
    public static async Task EnqueueRemoveAsync(
        NpgsqlDataSource controlPlane, string tenantId, string tenantDomain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controlPlane);
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        var payload = new JsonObject { ["tenantId"] = tenantId, ["tenantDomain"] = tenantDomain };
        await JobQueue.EnqueueAsync(connection, null, RemoveKind, payload, null, cancellationToken);
    }

    /// <summary>Reads a text of a job payload. A payload without it is a bug and fails the attempt.</summary>
    public static string Text(JsonObject payload, string name, string kind) =>
        payload[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException($"The {kind} payload has no {name}.");
}
