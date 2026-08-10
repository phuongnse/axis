using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Authorization.Infrastructure.Persistence;

public sealed class AuthorizationDbContextFactory : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    public AuthorizationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AuthorizationDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Authorization") ?? throw new InvalidOperationException("Set ConnectionStrings__Authorization.")).Options);
}
