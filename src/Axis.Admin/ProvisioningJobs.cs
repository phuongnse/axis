using System.Text.Json;
using Axis.Administration;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// One step of a provisioning job. <paramref name="RunAsync"/> gets the tenant id and the job's input JSON, which may be null, and
/// returns <see langword="null"/> on success or a refusal message. A refusal fails the job at once, and an exception that it throws ends only the claim.
/// </summary>
internal sealed record JobStep(string Name, Func<string, string?, CancellationToken, Task<string?>> RunAsync);

/// <summary>
/// The provisioning jobs of <c>platform.jobs</c>, which the publisher runs under <c>Tenancy:Source=directory</c>. A job is queued
/// with every step <c>pending</c>. <see cref="RunNextAsync"/> claims the oldest job that can run, with a lease, and runs one job at a
/// time for each tenant. It skips every step that already succeeded. A refusal from a step fails the job at once. A step that throws
/// ends the claim, and the next round claims the job again, up to three claims. The third throw fails the job. A running job whose
/// lease ran out after three claims is failed at the next claim. The <c>claims</c> count fences every update, so a publisher that lost
/// its claim changes nothing. The runner renews the lease at each step boundary. When a job ends, one platform audit record that
/// names the job's actor is written in the same transaction as its final state, whether it succeeded or failed.
/// </summary>
internal sealed class ProvisioningJobs(string platformConnectionString, IReadOnlyDictionary<string, IReadOnlyList<JobStep>> kinds, TimeSpan leaseDuration)
{
    private const int MaxClaims = 3;

    // Claims take this lock, so two publishers cannot both see no running job of a tenant.
    private const long ClaimLockKey = 0x4158_4953_4A4F_4253; // "AXISJOBS"

    private const string Pending = "pending";

    private const string Succeeded = "succeeded";

    private const string Failed = "failed";

    private static readonly string _claimedBy = $"{Environment.MachineName}-{Environment.ProcessId}";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private const string ExhaustedSql =
        """
        UPDATE platform.jobs
        SET state = 'failed', finished_at = clock_timestamp(), error = @message
        WHERE state = 'running' AND lease_until < clock_timestamp() AND claims >= @max
        RETURNING id, kind, tenant_id, actor
        """;

    // clock_timestamp() and not now(): now() is fixed when the transaction starts, before the wait for the lock.
    private const string ClaimSql =
        """
        WITH next AS (
          SELECT j.id FROM platform.jobs j
          WHERE (j.state = 'queued' OR (j.state = 'running' AND j.lease_until < clock_timestamp() AND j.claims < @max))
            AND NOT EXISTS (SELECT 1 FROM platform.jobs o WHERE o.tenant_id = j.tenant_id AND o.state = 'running' AND o.id <> j.id)
          ORDER BY j.created_at, j.id LIMIT 1
          FOR UPDATE SKIP LOCKED)
        UPDATE platform.jobs p
        SET state = 'running', claimed_by = @me, lease_until = clock_timestamp() + @lease,
            claims = p.claims + 1, started_at = coalesce(p.started_at, clock_timestamp())
        FROM next WHERE p.id = next.id
        RETURNING p.id, p.kind, p.tenant_id, p.steps::text, p.actor, p.claims, p.input::text
        """;

    // The claims count is the fencing token: it goes up on every claim, so a publisher that lost its claim changes no row.
    private const string RenewSql =
        """
        UPDATE platform.jobs
        SET steps = @steps, lease_until = clock_timestamp() + @lease
        WHERE id = @id AND state = 'running' AND claims = @claims
        """;

    // Ends the lease at once, so the next round claims the job again. The claims count still limits it to three.
    private const string ReleaseSql =
        """
        UPDATE platform.jobs
        SET lease_until = clock_timestamp()
        WHERE id = @id AND state = 'running' AND claims = @claims
        """;

    /// <summary>The kinds that the CLI commands also have: <c>suspend</c> and <c>resume</c>, each with one <c>status</c> step.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<JobStep>> Builtin(string platformConnectionString) =>
        new Dictionary<string, IReadOnlyList<JobStep>>
        {
            ["suspend"] = [new JobStep("status", (tenantId, _, cancellationToken) => StatusAsync(platformConnectionString, TenantStatus.SuspendChangeAsync, tenantId, cancellationToken))],
            ["resume"] = [new JobStep("status", (tenantId, _, cancellationToken) => StatusAsync(platformConnectionString, TenantStatus.ResumeChangeAsync, tenantId, cancellationToken))],
        };

    /// <summary>Adds a <c>queued</c> job with every step of <paramref name="kind"/> <c>pending</c>, and returns its id.</summary>
    public static async Task<Guid> QueueAsync(
        string platformConnectionString,
        IReadOnlyDictionary<string, IReadOnlyList<JobStep>> kinds,
        string kind,
        string tenantId,
        string actor,
        string? idempotencyKey,
        string? requestHash,
        CancellationToken cancellationToken,
        string? input = null)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (!kinds.TryGetValue(kind, out var steps))
        {
            throw new ArgumentException($"The job kind '{kind}' is not known.", nameof(kind));
        }

        var id = Guid.CreateVersion7();
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
        platform.Jobs.Add(new JobRow
        {
            Id = id,
            Kind = kind,
            TenantId = tenantId,
            State = "queued",
            Steps = JsonSerializer.Serialize(steps.Select(step => new StoredStep(step.Name, Pending, null)), _json),
            Actor = actor,
            Input = input,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await platform.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Claims and runs one job. Returns whether one was claimed.</summary>
    public async Task<bool> RunNextAsync(TextWriter output, CancellationToken cancellationToken)
    {
        var job = await ClaimAsync(output, cancellationToken);
        if (job is null)
        {
            return false;
        }

        output.WriteLine($"{job.TenantId} claim ok");
        try
        {
            await RunJobAsync(job, output, cancellationToken);
        }
        catch (Exception exception)
        {
            output.WriteLine($"{job.TenantId} claim failed: {TenantDatabaseSteps.Safe(exception)}");
            await ReleaseAsync(job, output);
        }

        return true;
    }

    private static async Task<string?> StatusAsync(
        string platformConnectionString,
        Func<NpgsqlConnection, string, CancellationToken, Task<StatusChange>> change,
        string tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await change(connection, tenantId, cancellationToken) switch
        {
            StatusChange.Unknown => TenantStatus.UnknownMessage,
            StatusChange.Provisioning => TenantStatus.ProvisioningMessage,
            _ => null,
        };
    }

    private async Task<ClaimedJob?> ClaimAsync(TextWriter output, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", ClaimLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await FailExhaustedAsync(connection, transaction, output, cancellationToken);

        ClaimedJob? claimed = null;
        await using (var command = new NpgsqlCommand(ClaimSql, connection, transaction))
        {
            command.Parameters.AddWithValue("me", _claimedBy);
            command.Parameters.AddWithValue("lease", leaseDuration);
            command.Parameters.AddWithValue("max", MaxClaims);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                claimed = new ClaimedJob(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return claimed;
    }

    // A running job that was claimed three times and whose lease ran out again ends failed, with its audit record.
    private static async Task FailExhaustedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TextWriter output, CancellationToken cancellationToken)
    {
        const string message = "The publisher stopped three times while running the job.";
        var exhausted = new List<(Guid Id, string Kind, string TenantId, string Actor)>();
        await using (var command = new NpgsqlCommand(ExhaustedSql, connection, transaction))
        {
            command.Parameters.AddWithValue("message", message);
            command.Parameters.AddWithValue("max", MaxClaims);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                exhausted.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        if (exhausted.Count == 0)
        {
            return;
        }

        // The records share the claim's transaction, so a job is never failed without its record.
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection).Options);
        await platform.Database.UseTransactionAsync(transaction, cancellationToken);
        foreach (var job in exhausted)
        {
            await PlatformAuditRecords.WriteAsync(platform, $"tenant {job.Kind}", job.TenantId, [$"job:{job.Id:D}"], job.Actor, cancellationToken);
            output.WriteLine($"{job.TenantId} claim failed: {message}");
        }
    }

    private async Task RunJobAsync(ClaimedJob job, TextWriter output, CancellationToken cancellationToken)
    {
        if (!kinds.TryGetValue(job.Kind, out var defined))
        {
            const string unknown = "The job kind is not known.";
            output.WriteLine($"{job.TenantId} claim failed: {unknown}");
            await FinishAsync(job, Failed, unknown, [], output, cancellationToken);
            return;
        }

        // The stored state of a step by name. A step that is not stored yet is pending.
        var stored = (JsonSerializer.Deserialize<List<StoredStep>>(job.Steps, _json) ?? []).ToDictionary(step => step.Name, StringComparer.Ordinal);
        var steps = defined.Select(step => stored.TryGetValue(step.Name, out var known) ? known : new StoredStep(step.Name, Pending, null)).ToList();
        for (var index = 0; index < defined.Count; index++)
        {
            if (steps[index].State == Succeeded)
            {
                continue;
            }

            string? refusal;
            try
            {
                refusal = await defined[index].RunAsync(job.TenantId, job.Input, cancellationToken);
            }
            catch (Exception exception)
            {
                var message = TenantDatabaseSteps.Safe(exception);
                output.WriteLine($"{job.TenantId} {defined[index].Name} failed: {message}");
                if (job.Claims < MaxClaims)
                {
                    await ReleaseAsync(job, output);
                    return;
                }

                steps[index] = steps[index] with { State = Failed, Error = message };
                await FinishAsync(job, Failed, message, steps, output, cancellationToken);
                return;
            }

            if (refusal is not null)
            {
                output.WriteLine($"{job.TenantId} {defined[index].Name} failed: {refusal}");
                steps[index] = steps[index] with { State = Failed, Error = refusal };
                await FinishAsync(job, Failed, refusal, steps, output, cancellationToken);
                return;
            }

            output.WriteLine($"{job.TenantId} {defined[index].Name} ok");
            steps[index] = steps[index] with { State = Succeeded, Error = null };
            if (!await RenewAsync(job, steps, output, cancellationToken))
            {
                return;
            }
        }

        await FinishAsync(job, Succeeded, null, steps, output, cancellationToken);
    }

    // Saves the finished steps and renews the lease. A job that another publisher claimed again changes nothing.
    private async Task<bool> RenewAsync(ClaimedJob job, IReadOnlyList<StoredStep> steps, TextWriter output, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platformConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RenewSql, connection);
        command.Parameters.Add(new NpgsqlParameter("steps", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(steps, _json) });
        command.Parameters.AddWithValue("lease", leaseDuration);
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("claims", job.Claims);
        if (await command.ExecuteNonQueryAsync(cancellationToken) > 0)
        {
            return true;
        }

        output.WriteLine($"{job.TenantId} claim failed: Another publisher claimed the job again.");
        return false;
    }

    // Ends the lease at once, so the next round claims the job again. A failure to do so leaves the lease to run out.
    private async Task ReleaseAsync(ClaimedJob job, TextWriter output)
    {
        try
        {
            await using var connection = new NpgsqlConnection(platformConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(ReleaseSql, connection);
            command.Parameters.AddWithValue("id", job.Id);
            command.Parameters.AddWithValue("claims", job.Claims);
            if (await command.ExecuteNonQueryAsync() > 0)
            {
                output.WriteLine($"{job.TenantId} job released: the job runs again in the next round.");
            }
        }
        catch (Exception exception)
        {
            output.WriteLine($"{job.TenantId} claim failed: {TenantDatabaseSteps.Safe(exception)}");
        }
    }

    // The final state and the audit record share one transaction, so an ended job always has its one record.
    private async Task FinishAsync(ClaimedJob job, string state, string? error, IReadOnlyList<StoredStep> steps, TextWriter output, CancellationToken cancellationToken)
    {
        await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        var stepsJson = JsonSerializer.Serialize(steps, _json);
        var finishedAt = DateTimeOffset.UtcNow;
        var changed = await platform.Jobs
            .Where(row => row.Id == job.Id && row.State == "running" && row.Claims == job.Claims)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.State, state)
                    .SetProperty(row => row.Error, error)
                    .SetProperty(row => row.Steps, stepsJson)
                    .SetProperty(row => row.FinishedAt, finishedAt),
                cancellationToken);
        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            output.WriteLine($"{job.TenantId} claim failed: Another publisher claimed the job again.");
            return;
        }

        await PlatformAuditRecords.WriteAsync(platform, $"tenant {job.Kind}", job.TenantId, [$"job:{job.Id:D}"], job.Actor, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record ClaimedJob(Guid Id, string Kind, string TenantId, string Steps, string Actor, int Claims, string? Input);

    private sealed record StoredStep(string Name, string State, string? Error);
}
