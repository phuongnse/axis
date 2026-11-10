using System.Text.Json.Nodes;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// Runs a <c>provisionTenant</c> job: creates the tenant of a confirmed sign-up in a cell, see
/// <c>docs/reference/sign-up.md#provisioning</c>. The payload is only <c>{ signUpId }</c>, and the job reads the rest from the sign-up. The
/// create call to the cell carries the subdomain as the tenant id, the chosen application unless it is the blank one, the person's email as
/// the administrator and the trial's quotas in key order, with the idempotency key <c>tenant-create:&lt;sign-up id&gt;</c>. The body is the same
/// on every run, as the cell refuses the same key with another body. A tenant that the directory holds already goes to its own cell again,
/// and not to a cell the <see cref="CellPicker"/> chooses, so a run that follows a crash never creates a second tenant. With no cell left the
/// sign-up is <c>queued</c> and the job queues itself again in 5 minutes and succeeds, so the wait for an operator does not use up its
/// retries. The job follows the cell's create job to its end and then queues the <c>tenantReady</c> email in the transaction that sets
/// <c>ready</c>, so a second run sends none. A failed cell job makes the sign-up <c>delayed</c>, logs an error that names the sign-up, the cell
/// and the cell's job, and queues the <c>provisioningDelayed</c> email in the transaction that sets <c>delayed</c>, so a second run sends none.
/// The run then succeeds, as a retry asks the cell for the same failed job. A sign-up that is not verified is skipped.
/// </summary>
internal sealed partial class ProvisionTenantJobHandler(
    [FromKeyedServices("directory")] NpgsqlDataSource directory,
    NpgsqlDataSource controlPlane,
    CellPicker picker,
    CellClients cells,
    EmailTemplates templates,
    ControlPlaneOptions options,
    TimeProvider clock,
    ILogger<ProvisionTenantJobHandler> logger) : IJobHandler
{
    public const string JobKind = "provisionTenant";

    /// <summary>How long a sign-up that found no cell waits before the job tries again.</summary>
    public static readonly TimeSpan NoCellWait = TimeSpan.FromMinutes(5);

    private const string CreatePath = "/api/cell/tenants";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Payload["signUpId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var signUpId))
        {
            throw new InvalidOperationException($"The {JobKind} payload has no signUpId.");
        }

        string fullName, email, subdomain, language, application;
        await using (var read = controlPlane.CreateCommand("""
            SELECT full_name, email, subdomain, language, application FROM control_plane.sign_ups
            WHERE id = @id AND state = 'verified' AND provisioning IS DISTINCT FROM 'ready'
            """))
        {
            read.Parameters.AddWithValue("id", signUpId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            (fullName, email, subdomain, language, application) =
                (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
        }

        var body = new JsonObject { ["id"] = subdomain, ["administrator"] = email };
        if (application.Length > 0)
        {
            body["application"] = application;
        }

        // Key order and a null kept as null, so every run sends the same bytes. No quota set is {}.
        body["quotas"] = new JsonObject(options.SignUp.TrialQuotas
            .OrderBy(quota => quota.Key, StringComparer.Ordinal)
            .Select(quota => KeyValuePair.Create(quota.Key, quota.Value is { } limit ? JsonValue.Create(limit) : (JsonNode?)null)));

        var operation = new CellOperation("tenant-create", signUpId);
        CellClient? client = null;
        CellJob? created = null;

        // A directory row means a cell accepted an earlier run: ask that cell again with the same key and body, whatever its room now.
        string? cellId;
        await using (var find = directory.CreateCommand("SELECT cell_id FROM directory.tenants WHERE id = @id"))
        {
            find.Parameters.AddWithValue("id", subdomain);
            cellId = await find.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (cellId is not null)
        {
            client = cells.Get(cellId);
            created = await client.PostAsync(CreatePath, operation, body, cancellationToken);
            await TenantRecordJobs.EnqueueWriteAsync(controlPlane, subdomain, cancellationToken);
        }
        else
        {
            var placement = await picker.PlaceAsync(
                options.Region!,
                subdomain,
                async (chosen, token) =>
                {
                    cellId = chosen;
                    client = cells.Get(chosen);
                    created = await client.PostAsync(CreatePath, operation, body, token);
                },
                cancellationToken);
            if (placement.State == CellPlacement.Queued)
            {
                await QueueAgainAsync(signUpId, cancellationToken);
                return;
            }
        }

        await SetProvisioningAsync(signUpId, "creating", cancellationToken);
        var ended = await client!.FollowAsync(created!.Id, cancellationToken);
        if (ended.State == CellJob.Failed)
        {
            // Never the job's error, the email or the body: the text of a failed job is stored with the job. The log comes before the
            // change, so a crash between the two never loses the alert.
            LogDelayed(signUpId, cellId!, created!.Id);
            await FinishAsync(
                signUpId,
                "delayed",
                "provisioningDelayed",
                new Dictionary<string, string> { ["name"] = fullName },
                language,
                email,
                cancellationToken);
            return;
        }

        await FinishAsync(
            signUpId,
            "ready",
            "tenantReady",
            new Dictionary<string, string> { ["name"] = fullName, ["tenantAddress"] = SignUpAddress.Of(options, subdomain) },
            language,
            email,
            cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "The sign-up {SignUpId} is delayed: the cell {CellId} ended its create job {CellJobId} as failed. The cell job holds the error.")]
    private partial void LogDelayed(Guid signUpId, string cellId, Guid cellJobId);

    // The progress and its email commit together, so a second run finds the progress set and queues no second email.
    private async Task FinishAsync(
        Guid signUpId,
        string progress,
        string template,
        IReadOnlyDictionary<string, string> values,
        string language,
        string email,
        CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var update = new NpgsqlCommand(
            """
            UPDATE control_plane.sign_ups SET provisioning = @progress
            WHERE id = @id AND provisioning IS DISTINCT FROM @progress AND provisioning IS DISTINCT FROM 'ready'
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("progress", progress);
            update.Parameters.AddWithValue("id", signUpId);

            // Another run already set it and queued the email, or the sign-up is ready, which delayed never replaces.
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        await EmailJobs.EnqueueAsync(connection, transaction, templates, template, language, email, values, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SetProvisioningAsync(Guid signUpId, string progress, CancellationToken cancellationToken)
    {
        // A delayed sign-up stays delayed, so a second run does not move it back and queue a second delayed email.
        await using var command = controlPlane.CreateCommand(
            "UPDATE control_plane.sign_ups SET provisioning = @progress WHERE id = @id AND provisioning IS DISTINCT FROM 'delayed'");
        command.Parameters.AddWithValue("progress", progress);
        command.Parameters.AddWithValue("id", signUpId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // The mark and the next job commit together, so a crash leaves either both or the run to repeat.
    private async Task QueueAgainAsync(Guid signUpId, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var queued = new NpgsqlCommand("UPDATE control_plane.sign_ups SET provisioning = 'queued' WHERE id = @id", connection, transaction))
        {
            queued.Parameters.AddWithValue("id", signUpId);
            await queued.ExecuteNonQueryAsync(cancellationToken);
        }

        await JobQueue.EnqueueAsync(
            connection, transaction, JobKind, new JsonObject { ["signUpId"] = signUpId.ToString() }, clock.GetUtcNow() + NoCellWait, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
