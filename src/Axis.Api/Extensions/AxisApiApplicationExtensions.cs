using Axis.Api.Endpoints;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Shared.Infrastructure.Observability;
using Axis.Shared.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;

namespace Axis.Api.Extensions;

internal static class AxisApiApplicationExtensions
{
    public static async Task RunAxisStartupTasksAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || EF.IsDesignTime)
            return;

        DbContextOptions<IdentityDbContext> identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(RequiredConnectionString(app.Configuration, "Identity"))
            .UseOpenIddict()
            .Options;
        await using IdentityDbContext identityDb = new(identityOptions);
        await identityDb.Database.MigrateAsync();

        DesignTimePublicSchemaWorkspaceContext publicWorkspaceContext = new();
        await MigrateWorkspaceModuleAsync<DataModelingDbContext>(
            RequiredConnectionString(app.Configuration, "DataModeling"),
            options => new DataModelingDbContext(options, publicWorkspaceContext));

        await MigrateWorkspaceModuleAsync<WorkflowBuilderDbContext>(
            RequiredConnectionString(app.Configuration, "WorkflowBuilder"),
            options => new WorkflowBuilderDbContext(options, publicWorkspaceContext));

        await MigrateWorkspaceModuleAsync<FormBuilderDbContext>(
            RequiredConnectionString(app.Configuration, "FormBuilder"),
            options => new FormBuilderDbContext(options, publicWorkspaceContext));

        await MigrateWorkspaceModuleAsync<WorkflowEngineDbContext>(
            RequiredConnectionString(app.Configuration, "WorkflowEngine"),
            options => new WorkflowEngineDbContext(options, publicWorkspaceContext));
    }

    private static async Task MigrateWorkspaceModuleAsync<TContext>(
        string connectionString,
        Func<DbContextOptions<TContext>, TContext> contextFactory)
        where TContext : DbContext
    {
        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using TContext context = contextFactory(options);
        await context.Database.MigrateAsync();
    }

    private static string RequiredConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Missing connection string '{name}'.");

    public static WebApplication UseAxisApiPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseAxisOpenTelemetry();
        app.UseMiddleware<ValidationExceptionMiddleware>();
        app.UseCors("SpaOrigin");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<WorkspaceAccessMiddleware>();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            app.UseSwagger();
            app.MapScalarApiReference(options =>
            {
                options.WithOpenApiRoutePattern("/swagger/v1/swagger.json");
                options.Title = "Axis API";
                options.Theme = ScalarTheme.Moon;
            });
        }

        return app;
    }

    public static WebApplication MapAxisApiEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
        });

        app.MapConnectEndpoints();

        app.MapAuthEndpoints();
        app.MapLegalEndpoints();
        app.MapPlanEndpoints();
        app.MapWorkspaceEndpoints();
        app.MapWorkspaceSettingsEndpoints();
        app.MapPlatformWorkspaceEndpoints();
        app.MapInvitationEndpoints();
        app.MapUserEndpoints();
        app.MapRoleEndpoints();
        app.MapModelEndpoints();
        app.MapDataClassEndpoints();
        app.MapRecordEndpoints();
        app.MapWorkflowEndpoints();
        app.MapExecutionEndpoints();
        app.MapFormEndpoints();
        app.MapFormTaskEndpoints();

        app.MapIdentityGrpc();
        app.MapDataModelingGrpc();
        app.MapFormBuilderGrpc();
        app.MapWorkflowBuilderGrpc();

        return app;
    }
}
