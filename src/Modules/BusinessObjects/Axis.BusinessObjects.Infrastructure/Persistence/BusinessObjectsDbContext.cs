using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Axis.BusinessObjects.Infrastructure.Persistence;

public sealed class BusinessObjectsDbContext(DbContextOptions<BusinessObjectsDbContext> options) : DbContext(options)
{
    public DbSet<BusinessObjectDefinition> BusinessObjectDefinitions => Set<BusinessObjectDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new BusinessObjectDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectFieldDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectChoiceOptionConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectFieldRuleConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectDefinitionVersionConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectDefinitionVersionFieldConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectDefinitionVersionChoiceOptionConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessObjectDefinitionVersionFieldRuleConfiguration());
    }
}
