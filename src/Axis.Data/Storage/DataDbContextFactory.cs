using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Data.Storage;

/// <summary>
/// Creates the context for <c>dotnet ef</c> so migrations can be generated. The connection string
/// is a placeholder; generating a migration does not connect.
/// </summary>
internal sealed class DataDbContextFactory : IDesignTimeDbContextFactory<DataDbContext>
{
    public DataDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DataDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_design")
            .Options);
}
