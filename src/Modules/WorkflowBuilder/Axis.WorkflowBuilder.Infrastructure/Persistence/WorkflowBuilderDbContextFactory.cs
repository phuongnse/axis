using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence;

/// <summary>Design-time factory for <c>dotnet ef migrations</c> only.</summary>
internal sealed class WorkflowBuilderDbContextFactory : IDesignTimeDbContextFactory<WorkflowBuilderDbContext>
{
    public WorkflowBuilderDbContext CreateDbContext(string[] args)
    {
        string connectionString = RequireConnectionString(
            "ConnectionStrings__WorkflowBuilder",
            "WORKFLOWBUILDER_CONNECTION_STRING");

        DbContextOptionsBuilder<WorkflowBuilderDbContext> builder = new();
        builder.UseNpgsql(connectionString);
        return new WorkflowBuilderDbContext(builder.Options, new DesignTimePublicSchemaTenantContext());
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
