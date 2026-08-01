using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Axis.Rules.Infrastructure.Persistence;

public sealed class RulesDbContext(DbContextOptions<RulesDbContext> options) : DbContext(options)
{
    public DbSet<RuleDefinition> RuleDefinitions => Set<RuleDefinition>();
    public DbSet<RuleBinding> RuleBindings => Set<RuleBinding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RuleDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new RuleDefinitionVersionConfiguration());
        modelBuilder.ApplyConfiguration(new RuleBindingConfiguration());
    }
}
