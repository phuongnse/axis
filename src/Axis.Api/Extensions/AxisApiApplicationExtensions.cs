using Axis.Api.Endpoints;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Shared.Infrastructure.Observability;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
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

        using IServiceScope scope = app.Services.CreateScope();
        IdentityDbContext identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityDb.Database.MigrateAsync();
    }

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
        app.UseMiddleware<TenantTeamAccountAccessMiddleware>();
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
        app.MapTeamAccountEndpoints();
        app.MapTeamAccountSettingsEndpoints();
        app.MapPlatformTeamAccountEndpoints();
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
