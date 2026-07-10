using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Rules.Infrastructure.Persistence;

public sealed class RulesDbContextFactory : IDesignTimeDbContextFactory<RulesDbContext>
{
    public RulesDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Rules")
            ?? Environment.GetEnvironmentVariable("RULES_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__Rules or RULES_CONNECTION_STRING for design-time Rules migrations.");

        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new RulesDbContext(options);
    }
}
