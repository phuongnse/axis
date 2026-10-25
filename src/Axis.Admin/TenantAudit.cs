using Axis.Administration;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;

namespace Axis.Admin;

/// <summary>
/// Writes the platform audit record of one command for one tenant, before the command changes anything for that tenant. A
/// success prints nothing, so the step lines of the commands stay as they are. A failure prints one <c>audit failed</c> line
/// that names the tenant and never a connection string, and the caller then stops work for that tenant.
/// </summary>
internal sealed class TenantAudit(string command, string platformConnectionString)
{
    public async Task<bool> WriteAsync(string tenantId, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A context for each record: commands that migrate in parallel share one audit, and a failed record is never tracked again.
            await using var platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options);
            await PlatformAuditRecords.WriteAsync(platform, command, tenantId, [], cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            // A save failure wraps the database error, whose message is the one that helps the operator.
            var cause = exception is DbUpdateException { InnerException: { } inner } ? inner : exception;
            output.WriteLine($"{tenantId} audit failed: {TenantDatabaseSteps.Safe(cause)}");
            return false;
        }
    }
}
