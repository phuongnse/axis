using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Axis.Objects.Infrastructure.Persistence;

public sealed class ObjectsDbContext(DbContextOptions<ObjectsDbContext> options) : DbContext(options)
{
    public DbSet<ObjectDefinition> ObjectDefinitions => Set<ObjectDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ObjectDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new ObjectFieldDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new ObjectDefinitionVersionConfiguration());
        modelBuilder.ApplyConfiguration(new ObjectDefinitionVersionFieldConfiguration());
    }
}
