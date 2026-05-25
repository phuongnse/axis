using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.Identity.Infrastructure.Persistence;

/// <summary>Design-time factory for <c>dotnet ef migrations</c> only.</summary>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        string? connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Identity")
            ?? Environment.GetEnvironmentVariable("IDENTITY_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__Identity or IDENTITY_CONNECTION_STRING before running dotnet ef.");
        }

        DbContextOptionsBuilder<IdentityDbContext> builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.UseNpgsql(connectionString).UseOpenIddict();
        return new IdentityDbContext(builder.Options);
    }
}
