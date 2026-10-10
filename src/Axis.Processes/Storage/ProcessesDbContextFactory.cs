using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Processes.Storage;

/// <summary>
/// Creates the context for <c>dotnet ef</c> so migrations can be generated. The connection string
/// is a placeholder; generating a migration does not connect.
/// </summary>
internal sealed class ProcessesDbContextFactory : IDesignTimeDbContextFactory<ProcessesDbContext>
{
    public ProcessesDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProcessesDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_design")
            .Options);
}
