using Axis.Administration;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Admin;

/// <summary>
/// The check of the pending host requests in <c>platform.tenant_host_requests</c>, which the publisher runs under
/// <c>Tenancy:Source=directory</c>, see <see cref="Publisher"/>. <see cref="RunRoundAsync"/> first ends every pending request past its
/// <c>expires_at</c> as <c>expired</c>, with no DNS lookup, so its host keeps answering <c>404</c>. It then claims the pending requests that
/// no round checked for one interval, by setting <c>checked_at</c> in an <c>UPDATE</c> that skips locked rows. Two publishers never check a
/// request twice, and a restart does not repeat a lookup before the interval is up. For each claimed request it reads the <c>TXT</c> records
/// of <c>_axis-verify.</c> and the host. A wrong token, no record or a failed lookup leaves the request <c>pending</c> and writes one line.
/// When a record equals the token, the host is added to the tenant with the platform audit record <c>tenant host verify</c> that names the
/// operator who asked for it, then the realm's redirect addresses are written, then the certificate of the host is written when the publisher has a writer, and last the request
/// becomes <c>verified</c>. A host that the tenant
/// already holds is not added again, but only a request whose record still matches is verified. A host that another tenant holds, a
/// tenant that is still provisioning, a realm that fails and a certificate that fails leave the request <c>pending</c>, and the next round
/// repeats only the steps that did not finish. A certificate that exists counts as written, so a repeat is safe.
/// </summary>
internal sealed class TenantHostVerification(
    string platformConnectionString,
    TenantSecretCipher cipher,
    HttpClient http,
    IDnsTxtResolver resolver,
    TimeSpan interval,
    ICertificateWriter? certificates)
{
    internal const string Command = "tenant host verify";

    internal const string RecordPrefix = "_axis-verify.";

    private const string ExpireSql =
        """
        UPDATE platform.tenant_host_requests
        SET state = 'expired'
        WHERE state = 'pending' AND expires_at <= now()
        RETURNING tenant_id, host
        """;

    // checked_at is the claim: a row that another publisher locked is skipped, and a row checked within one interval is not due.
    private const string ClaimSql =
        """
        WITH due AS (
          SELECT tenant_id, host, requested_at FROM platform.tenant_host_requests
          WHERE state = 'pending' AND expires_at > now() AND (checked_at IS NULL OR checked_at <= now() - @interval)
          FOR UPDATE SKIP LOCKED)
        UPDATE platform.tenant_host_requests r
        SET checked_at = now()
        FROM due
        WHERE r.tenant_id = due.tenant_id AND r.host = due.host AND r.requested_at = due.requested_at
        RETURNING r.tenant_id, r.host, r.requested_at, r.token, r.actor
        """;

    private const string VerifiedSql =
        """
        UPDATE platform.tenant_host_requests
        SET state = 'verified'
        WHERE tenant_id = @tenant AND host = @host AND requested_at = @requested AND state = 'pending'
        """;

    /// <summary>How long the publisher waits between two rounds, and how long a request waits before it is checked again.</summary>
    public TimeSpan Interval => interval;

    private sealed record Claimed(string TenantId, string Host, DateTimeOffset RequestedAt, string Token, string Actor);

    /// <summary>Ends the requests that ran out, then checks the requests that are due, one at a time.</summary>
    public async Task RunRoundAsync(TextWriter output, CancellationToken cancellationToken)
    {
        foreach (var (tenantId, host) in await ExpireAsync(cancellationToken))
        {
            output.WriteLine($"{tenantId} host expired: {host}");
        }

        foreach (var request in await ClaimAsync(cancellationToken))
        {
            try
            {
                await CheckAsync(request, output, cancellationToken);
            }
            catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or DbUpdateException)
            {
                output.WriteLine($"{request.TenantId} host check failed: {TenantDatabaseSteps.Safe(exception)}");
            }
        }
    }

    private async Task<List<(string TenantId, string Host)>> ExpireAsync(CancellationToken cancellationToken)
    {
        var expired = new List<(string, string)>();
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ExpireSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            expired.Add((reader.GetString(0), reader.GetString(1)));
        }

        return expired;
    }

    private async Task<List<Claimed>> ClaimAsync(CancellationToken cancellationToken)
    {
        var claimed = new List<Claimed>();
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ClaimSql, connection);
        command.Parameters.Add(new NpgsqlParameter("interval", NpgsqlDbType.Interval) { Value = interval });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claimed.Add(new Claimed(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return claimed;
    }

    // The lookup comes first, so a host is never verified without a record that matches. Every later step can be repeated.
    private async Task CheckAsync(Claimed request, TextWriter output, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> values;
        try
        {
            values = await resolver.ResolveAsync(RecordPrefix + request.Host, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            output.WriteLine($"{request.TenantId} host check failed: {TenantDatabaseSteps.Safe(exception)}");
            return;
        }

        if (!values.Any(value => string.Equals(value, request.Token, StringComparison.Ordinal)))
        {
            return;
        }

        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
        string[] keys = [request.Host.ToLowerInvariant()];
        var holder = await platform.TenantHosts.AsNoTracking()
            .Where(row => keys.Contains(row.Host.ToLower()))
            .Select(row => row.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (holder is not null && holder != request.TenantId)
        {
            output.WriteLine($"{request.TenantId} host stays pending: {request.Host} belongs to another tenant.");
            return;
        }

        if (holder is null)
        {
            var added = await TenantHostCommands.AddAsync(platform, Command, request.TenantId, request.Host, sitePath: null, request.Actor, cancellationToken);
            if (added.Outcome != TenantSettingOutcome.Changed)
            {
                output.WriteLine($"{request.TenantId} host stays pending: {added.Message}");
                return;
            }
        }

        if (!await SyncRealmAsync(request.TenantId, output, cancellationToken))
        {
            return;
        }

        if (certificates is not null)
        {
            try
            {
                await certificates.WriteAsync(request.TenantId, request.Host, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException)
            {
                output.WriteLine($"{request.TenantId} certificate failed: {TenantDatabaseSteps.Safe(exception)}");
                return;
            }
        }

        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var verified = new NpgsqlCommand(VerifiedSql, connection);
        verified.Parameters.AddWithValue("tenant", request.TenantId);
        verified.Parameters.AddWithValue("host", request.Host);
        verified.Parameters.AddWithValue("requested", request.RequestedAt);
        if (await verified.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            output.WriteLine($"{request.TenantId} host verified: {request.Host}");
        }
    }

    // Writes the redirect addresses of the tenant's realm from all its hosts, as the host commands do. A tenant without a realm has nothing to write.
    private async Task<bool> SyncRealmAsync(string tenantId, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
            var row = await platform.Tenants.AsNoTracking().SingleOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);
            if (row is null || DirectoryTenantCatalog.Realm(row, cipher) is not { } realm)
            {
                return true;
            }

            var hosts = await platform.TenantHosts.AsNoTracking().Where(host => host.TenantId == tenantId).Select(host => host.Host).ToListAsync(cancellationToken);
            await new RealmSync(http, realm.BaseAddress).SyncPlatformAddressesAsync(realm.Name, realm.ActivationClientSecret.Reveal(), hosts, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or NpgsqlException)
        {
            output.WriteLine($"{tenantId} realm failed: {TenantDatabaseSteps.Safe(exception)}");
            return false;
        }
    }
}
