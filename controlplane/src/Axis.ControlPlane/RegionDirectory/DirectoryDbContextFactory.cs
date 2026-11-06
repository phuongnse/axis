using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.ControlPlane.RegionDirectory;

/// <summary>
/// Creates the context for <c>dotnet ef</c> so migrations can be generated. The connection string
/// is a placeholder; generating a migration does not connect.
/// </summary>
internal sealed class DirectoryDbContextFactory : IDesignTimeDbContextFactory<DirectoryDbContext>
{
    public DirectoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_directory_design")
            .Options);
}
