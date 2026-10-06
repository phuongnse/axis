using Axis.Server.Applications;
using Axis.Server.Health;
using Axis.Server.Tenancy;
using Axis.Tenancy;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var platformConnectionString = builder.Configuration.GetConnectionString("Platform");
if (string.IsNullOrWhiteSpace(platformConnectionString))
{
    throw new InvalidOperationException("Connection string 'Platform' is not configured.");
}

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(platformConnectionString));
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);
builder.Services.AddProblemDetails();
builder.Services.AddTenancy(builder.Configuration);
builder.Services.AddScoped<TenantDatabase>();
builder.Services.AddSingleton<ActiveModelCache>();
builder.Services.AddScoped<ActiveApplicationResolver>();

var app = builder.Build();

// First, so API and SPA paths alike need a known tenant host.
app.UseTenantResolution();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
