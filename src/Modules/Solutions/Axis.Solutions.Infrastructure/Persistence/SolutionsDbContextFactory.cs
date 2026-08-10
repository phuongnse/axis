using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Solutions.Infrastructure.Persistence;

public sealed class SolutionsDbContextFactory : IDesignTimeDbContextFactory<SolutionsDbContext>
{
    public SolutionsDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__Solutions")
            ?? Environment.GetEnvironmentVariable("SOLUTIONS_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Solutions or SOLUTIONS_CONNECTION_STRING for design-time Solutions migrations.");
        return new SolutionsDbContext(new DbContextOptionsBuilder<SolutionsDbContext>().UseNpgsql(connection).Options);
    }
}
