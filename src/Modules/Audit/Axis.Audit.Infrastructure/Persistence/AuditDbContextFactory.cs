using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Audit.Infrastructure.Persistence;

public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Audit")
            ?? Environment.GetEnvironmentVariable("AUDIT_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__Audit or AUDIT_CONNECTION_STRING for design-time Audit migrations.");

        DbContextOptions<AuditDbContext> options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AuditDbContext(options);
    }
}
