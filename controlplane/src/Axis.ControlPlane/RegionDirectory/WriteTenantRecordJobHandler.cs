using Axis.ControlPlane.Dns;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Runs a <c>writeTenantRecord</c> job: sets the <c>CNAME</c> of the tenant's subdomain to its cell's ingress, with the cell and the
/// tenant domain that the directory holds when the job runs. A tenant with no row in the directory gets no record. The job is
/// idempotent, and a run makes the record match the directory whenever it runs.
/// </summary>
internal sealed class WriteTenantRecordJobHandler([FromKeyedServices("directory")] NpgsqlDataSource directory, IDnsRecords dns) : IJobHandler
{
    public string Kind => TenantRecordJobs.WriteKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var tenantId = TenantRecordJobs.Text(job.Payload, "tenantId", Kind);

        await using var command = directory.CreateCommand("""
            SELECT t.cell_id, r.tenant_domain
            FROM directory.tenants t JOIN directory.regions r ON r.code = t.region_code
            WHERE t.id = @id
            """);
        command.Parameters.AddWithValue("id", tenantId);
        string cellId;
        string tenantDomain;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            cellId = reader.GetString(0);
            tenantDomain = reader.GetString(1);
        }

        await dns.SetCnameAsync(TenantRecordJobs.Name(tenantId, tenantDomain), TenantRecordJobs.Target(cellId, tenantDomain), cancellationToken);
    }
}
