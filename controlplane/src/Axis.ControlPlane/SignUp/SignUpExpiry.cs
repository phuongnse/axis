using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// Runs the recurring <c>expireSignUps</c> job: marks each pending sign-up whose 24 hours are over <c>expired</c>, see
/// <c>docs/reference/sign-up.md#verifying-the-email-first</c>. Its subdomain is free again. The subdomain check and a new sign-up
/// already ignore a pending sign-up past its end, so the job only keeps the table true. The payload is not used.
/// </summary>
internal sealed class SignUpExpiry(NpgsqlDataSource controlPlane) : IJobHandler
{
    public const string JobKind = "expireSignUps";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        await using var command = controlPlane.CreateCommand(
            "UPDATE control_plane.sign_ups SET state = 'expired' WHERE state = 'pending' AND expires_at <= now()");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
