using Axis.ControlPlane.Dns;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Runs a <c>removeTenantRecord</c> job: removes the <c>CNAME</c> of the tenant's subdomain. A tenant that has a row in the
/// directory again when the job runs keeps its record. The job is idempotent, and a run makes the record match the directory
/// whenever it runs.
/// </summary>
internal sealed class RemoveTenantRecordJobHandler([FromKeyedServices("directory")] NpgsqlDataSource directory, IDnsRecords dns) : IJobHandler
{
    public string Kind => TenantRecordJobs.RemoveKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenantId = TenantRecordJobs.Text(job.Payload, "tenantId", Kind);
        var tenantDomain = TenantRecordJobs.Text(job.Payload, "tenantDomain", Kind);

        await using var command = directory.CreateCommand("SELECT 1 FROM directory.tenants WHERE id = @id");
        command.Parameters.AddWithValue("id", tenantId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
        {
            return;
        }

        await dns.RemoveCnameAsync(TenantRecordJobs.Name(tenantId, tenantDomain), cancellationToken);
    }
}
