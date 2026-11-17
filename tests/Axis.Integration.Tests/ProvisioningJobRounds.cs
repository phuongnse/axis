using Axis.Admin;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>Runs the rounds of a provisioning job as the publisher does, and reads the state of the job.</summary>
internal static class ProvisioningJobRounds
{
    // MinIO at times fails a request. A step that throws releases the job, and the publisher claims it again in the next round, up to three claims.
    public static async Task RunAsync(ProvisioningJobs runner, string platform, Guid job, TextWriter output, CancellationToken cancellationToken, int rounds = 3)
    {
        for (var round = 1; round <= rounds; round++)
        {
            Assert.True(await runner.RunNextAsync(output, cancellationToken));
            if (await StateAsync(platform, job, cancellationToken) != "running")
            {
                break;
            }
        }
    }

    public static async Task<string?> StateAsync(string platform, Guid job, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT state FROM platform.jobs WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", job);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }
}
