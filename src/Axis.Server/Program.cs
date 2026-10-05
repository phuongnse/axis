using Axis.Server.Health;
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

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
