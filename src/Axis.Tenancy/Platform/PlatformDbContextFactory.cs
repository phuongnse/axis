using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Creates the context for <c>dotnet ef</c> so migrations can be generated. The connection string
/// is a placeholder; generating a migration does not connect.
/// </summary>
internal sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_design")
            .Options);
}
