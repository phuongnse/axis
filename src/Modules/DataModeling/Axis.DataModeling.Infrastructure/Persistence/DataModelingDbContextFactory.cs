using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.DataModeling.Infrastructure.Persistence;

/// <summary>Design-time factory for <c>dotnet ef migrations</c> only.</summary>
internal sealed class DataModelingDbContextFactory : IDesignTimeDbContextFactory<DataModelingDbContext>
{
    public DataModelingDbContext CreateDbContext(string[] args)
    {
        string connectionString = RequireConnectionString(
            "ConnectionStrings__DataModeling",
            "DATAMODELING_CONNECTION_STRING");

        DbContextOptionsBuilder<DataModelingDbContext> builder = new();
        builder.UseNpgsql(connectionString);
        return new DataModelingDbContext(builder.Options, new DesignTimePublicSchemaTenantContext());
    }

    private static string RequireConnectionString(params string[] envVarNames)
    {
        foreach (string name in envVarNames)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            $"Set {string.Join(" or ", envVarNames)} before running dotnet ef.");
    }
}
