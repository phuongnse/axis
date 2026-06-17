using Axis.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Axis.WorkflowEngine.Infrastructure.Persistence;

/// <summary>Design-time factory for <c>dotnet ef migrations</c> only.</summary>
internal sealed class WorkflowEngineDbContextFactory : IDesignTimeDbContextFactory<WorkflowEngineDbContext>
{
    public WorkflowEngineDbContext CreateDbContext(string[] args)
    {
        string connectionString = RequireConnectionString(
            "ConnectionStrings__WorkflowEngine",
            "WORKFLOWENGINE_CONNECTION_STRING");

        DbContextOptionsBuilder<WorkflowEngineDbContext> builder = new();
        builder.UseNpgsql(connectionString);
        return new WorkflowEngineDbContext(builder.Options, new DesignTimePublicSchemaWorkspaceContext());
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
