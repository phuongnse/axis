using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.FormBuilder.Infrastructure.Persistence;

/// <summary>Design-time factory for <c>dotnet ef migrations</c> only.</summary>
internal sealed class FormBuilderDbContextFactory : IDesignTimeDbContextFactory<FormBuilderDbContext>
{
    public FormBuilderDbContext CreateDbContext(string[] args)
    {
        string connectionString = RequireConnectionString(
            "ConnectionStrings__FormBuilder",
            "FORMBUILDER_CONNECTION_STRING");

        DbContextOptionsBuilder<FormBuilderDbContext> builder = new();
        builder.UseNpgsql(connectionString);
        return new FormBuilderDbContext(builder.Options, new DesignTimePublicSchemaTenantContext());
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
