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
            ?? "Host=localhost;Port=5432;Database=axis_objects;Username=axis;Password=axis";

        DbContextOptions<ObjectsDbContext> options = new DbContextOptionsBuilder<ObjectsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ObjectsDbContext(options);
    }
}
