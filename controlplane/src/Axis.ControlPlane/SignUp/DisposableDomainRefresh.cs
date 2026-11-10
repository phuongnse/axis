using System.Security.Cryptography;
using System.Text;
using Axis.ControlPlane.Jobs;
using Npgsql;
using NpgsqlTypes;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// Runs the recurring <c>refreshDisposableDomains</c> job: downloads the list of disposable mailbox domains from
/// <c>ControlPlane:SignUp:DisposableDomains:Url</c> and, only when its SHA-256 is <c>ControlPlane:SignUp:DisposableDomains:Sha256</c>,
/// replaces <c>control_plane.disposable_domains</c> in one transaction, see <c>docs/reference/sign-up.md#bots-and-abuse</c>. A line is a
/// domain: it is trimmed and lower-cased, and a blank line, a line that starts with <c>#</c> and a repeat are skipped. A list with another hash
/// keeps the table and logs an error, and the job succeeds, as the pinned hash would fail again. A failed download throws, so the job runner
/// retries it. The payload is not used.
/// </summary>
internal sealed partial class DisposableDomainRefresh(
    NpgsqlDataSource controlPlane,
    IHttpClientFactory httpClients,
    ControlPlaneOptions options,
    ILogger<DisposableDomainRefresh> logger) : IJobHandler
{
    public const string JobKind = "refreshDisposableDomains";

    public const string HttpClientName = "disposableDomains";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var settings = options.SignUp.DisposableDomains;
        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.GetAsync(settings.Url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), settings.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            LogHashMismatch();
            return;
        }

        var domains = Encoding.UTF8.GetString(bytes)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // DELETE, not TRUNCATE: the sign-up check goes on reading the old rows until the commit.
        await using (var delete = new NpgsqlCommand("DELETE FROM control_plane.disposable_domains", connection, transaction))
        {
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand("INSERT INTO control_plane.disposable_domains (domain) SELECT unnest(@domains)", connection, transaction))
        {
            insert.Parameters.Add(new NpgsqlParameter("domains", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = domains });
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "The disposable domain list does not match the setting ControlPlane:SignUp:DisposableDomains:Sha256. The table stays as it is.")]
    private partial void LogHashMismatch();
}
