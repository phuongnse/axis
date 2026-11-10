using System.Globalization;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Subscriptions;
using Npgsql;

namespace Axis.ControlPlane.Admin;

/// <summary>
/// <c>trial extend</c>, <c>email resend</c> and <c>quota override</c>: the commands of the platform operator that change the control plane, see
/// <c>docs/reference/control-plane.md#operators</c>. Each commits its change and one audit record of the actor kind <c>operator</c>, with the
/// <c>--operator</c> name as actor, in one transaction. A refusal changes nothing, writes no audit record and is exit code 1. Nothing printed
/// holds an email address, a name or the values of an email.
/// </summary>
internal static class OperatorCommands
{
    private const string TrialExtendedAction = "subscription.trialExtended";

    private const string EmailResentAction = "email.resent";

    private const string QuotaOverriddenAction = "quota.overridden";

    public static async Task<int> TrialExtendAsync(
        ControlPlaneOptions options,
        string tenant,
        int days,
        string operatorName,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // The step id stays as it is. It is the idempotency key of the clock's next call to the cell, and a new one would make a move
            // that has already called the cell fail to commit, which leaves a read-only tenant under a trialing subscription.
            Guid id;
            DateTimeOffset trialEndsAt;
            DateTimeOffset nextAt;
            await using (var command = new NpgsqlCommand(
                """
                UPDATE control_plane.subscriptions
                SET trial_ends_at = trial_ends_at + make_interval(days => @days), next_at = next_at + make_interval(days => @days)
                WHERE tenant_id = @tenant AND state = 'trialing'
                RETURNING id, trial_ends_at, next_at
                """,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("days", days);
                command.Parameters.AddWithValue("tenant", tenant);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    error.WriteLine($"The tenant {tenant} has no trialing subscription.");
                    return 1;
                }

                id = reader.GetGuid(0);
                trialEndsAt = reader.GetFieldValue<DateTimeOffset>(1);
                nextAt = reader.GetFieldValue<DateTimeOffset>(2);
            }

            await AuditRecords.AppendAsync(transaction, AuditActorKind.Operator, operatorName, TrialExtendedAction, id.ToString(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            output.WriteLine($"{id}  trial ends {RegionLists.Time(trialEndsAt)}  next {RegionLists.Time(nextAt)}");
            return 0;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(error, "the trial");
        }
    }

    public static async Task<int> EmailResendAsync(
        ControlPlaneOptions options,
        Guid jobId,
        string operatorName,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            string kind;
            string state;
            string payloadText;
            await using (var command = new NpgsqlCommand("SELECT kind, state, payload::text FROM control_plane.jobs WHERE id = @id", connection, transaction))
            {
                command.Parameters.AddWithValue("id", jobId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    error.WriteLine($"There is no job {jobId}.");
                    return 1;
                }

                kind = reader.GetString(0);
                state = reader.GetString(1);
                payloadText = reader.GetString(2);
            }

            if (kind != EmailJobs.Kind)
            {
                error.WriteLine($"The job {jobId} is not a {EmailJobs.Kind} job.");
                return 1;
            }

            // A job that is queued or running is still on its way, so a copy would send the email twice.
            if (state is not ("succeeded" or "failed"))
            {
                error.WriteLine($"The job {jobId} has not ended, it is {state}.");
                return 1;
            }

            Guid newId;
            try
            {
                var payload = JsonNode.Parse(payloadText) as JsonObject ?? [];
                var values = (payload["values"] as JsonObject ?? [])
                    .ToDictionary(value => value.Key, value => value.Value?.GetValue<string>() ?? string.Empty, StringComparer.Ordinal);

                // The payload is checked again against today's templates. The message holds no part of it.
                newId = await EmailJobs.EnqueueAsync(
                    connection,
                    transaction,
                    EmailTemplates.LoadEmbedded(),
                    payload["template"]?.GetValue<string>() ?? string.Empty,
                    payload["language"]?.GetValue<string>() ?? string.Empty,
                    payload["to"]?.GetValue<string>() ?? string.Empty,
                    values,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or System.Text.Json.JsonException)
            {
                // Never the exception's text: it can quote the address.
                error.WriteLine($"The payload of the job {jobId} cannot be queued again.");
                return 1;
            }

            await AuditRecords.AppendAsync(transaction, AuditActorKind.Operator, operatorName, EmailResentAction, jobId.ToString(), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            output.WriteLine($"Queued the email of job {jobId} again as job {newId}.");
            return 0;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(error, "the email");
        }
    }

    public static async Task<int> QuotaOverrideAsync(
        ControlPlaneOptions options,
        string tenant,
        string quota,
        long value,
        DateTimeOffset until,
        string reason,
        string operatorName,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // A subscription that is deleting or deleted gets no quota writes, so an override for it would change nothing.
            Guid id;
            await using (var command = new NpgsqlCommand(
                "SELECT id FROM control_plane.subscriptions WHERE tenant_id = @tenant AND state NOT IN ('deleting', 'deleted') AND plan_code IS NOT NULL",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("tenant", tenant);
                if (await command.ExecuteScalarAsync(cancellationToken) is not Guid found)
                {
                    error.WriteLine($"The tenant {tenant} has no subscription with a plan.");
                    return 1;
                }

                id = found;
            }

            if (until <= DateTimeOffset.UtcNow)
            {
                error.WriteLine("The end date must be in the future.");
                return 1;
            }

            await using (var upsert = new NpgsqlCommand(
                """
                INSERT INTO control_plane.quota_overrides (subscription_id, quota, value, reason, until, operator)
                VALUES (@id, @quota, @value, @reason, @until, @operator)
                ON CONFLICT (subscription_id, quota) DO UPDATE
                SET value = EXCLUDED.value, reason = EXCLUDED.reason, until = EXCLUDED.until, operator = EXCLUDED.operator, created_at = now()
                """,
                connection,
                transaction))
            {
                upsert.Parameters.AddWithValue("id", id);
                upsert.Parameters.AddWithValue("quota", quota);
                upsert.Parameters.AddWithValue("value", value);
                upsert.Parameters.AddWithValue("reason", reason);
                upsert.Parameters.AddWithValue("until", until);
                upsert.Parameters.AddWithValue("operator", operatorName);
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditRecords.AppendAsync(transaction, AuditActorKind.Operator, operatorName, QuotaOverriddenAction, id.ToString(), cancellationToken);
            var jobId = await SubscriptionQuotas.EnqueueAsync(connection, transaction, id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Never the reason: it is free text that can name a person.
            output.WriteLine($"{id}  {quota} {value.ToString(CultureInfo.InvariantCulture)} until {RegionLists.Time(until)}  job {jobId}");
            return 0;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(error, "the quota");
        }
    }

    // Never the exception's text: an Npgsql message can name the host.
    private static int Failed(TextWriter error, string what)
    {
        error.WriteLine($"The setting {ControlPlaneOptions.SectionName}:{nameof(ControlPlaneOptions.ConnectionString)} could not be used to change {what}.");
        return 1;
    }
}
