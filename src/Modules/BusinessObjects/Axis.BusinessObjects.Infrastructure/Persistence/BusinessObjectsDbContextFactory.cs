using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.BusinessObjects.Infrastructure.Persistence;

public sealed class BusinessObjectsDbContextFactory : IDesignTimeDbContextFactory<BusinessObjectsDbContext>
{
    public BusinessObjectsDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__BusinessObjects")
            ?? Environment.GetEnvironmentVariable("BUSINESS_OBJECTS_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__BusinessObjects or BUSINESS_OBJECTS_CONNECTION_STRING for design-time Business Objects migrations.");

        DbContextOptions<BusinessObjectsDbContext> options = new DbContextOptionsBuilder<BusinessObjectsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new BusinessObjectsDbContext(options);
    }
}
