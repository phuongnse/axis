using Axis.Api.Endpoints;
using Axis.Api.Middleware;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Objects.Infrastructure.Persistence;
using Axis.Shared.Infrastructure.Observability;
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

        DbContextOptions<ObjectsDbContext> objectsOptions = new DbContextOptionsBuilder<ObjectsDbContext>()
            .UseNpgsql(RequiredConnectionString(app.Configuration, "Objects"))
            .Options;
        await using ObjectsDbContext objectsDb = new(objectsOptions);
        await objectsDb.Database.MigrateAsync();
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
        app.UseAuthorization();

        if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
        {
            app.UseSwagger();
            app.MapScalarApiReference(options =>
            {
                options.WithOpenApiRoutePattern("/swagger/v1/swagger.json");
                options.Title = "Axis Platform API";
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
        app.MapUserEndpoints();
        app.MapFieldRuleDefinitionEndpoints();
        app.MapObjectDefinitionEndpoints();

        return app;
    }
}
