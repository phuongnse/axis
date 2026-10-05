using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Configuration.Storage;

/// <summary>
/// Creates the context for <c>dotnet ef</c> so migrations can be generated. The connection string
/// is a placeholder; generating a migration does not connect.
/// </summary>
internal sealed class ConfigurationDbContextFactory : IDesignTimeDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_design")
            .Options);
}
