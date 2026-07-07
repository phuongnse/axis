using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Objects.Infrastructure.Persistence;

public sealed class ObjectsDbContextFactory : IDesignTimeDbContextFactory<ObjectsDbContext>
{
    public ObjectsDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Objects")
            ?? Environment.GetEnvironmentVariable("OBJECTS_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__Objects or OBJECTS_CONNECTION_STRING for design-time Objects migrations.");

        DbContextOptions<ObjectsDbContext> options = new DbContextOptionsBuilder<ObjectsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ObjectsDbContext(options);
    }
}
