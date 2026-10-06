using Axis.Presentation.Sites;
using Axis.Presentation.Texts;
using Axis.Server.Applications;
using Axis.Server.Health;
using Axis.Server.Presentation;
using Axis.Server.Records;
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
builder.Services.AddSingleton<ISiteMetadataProvider, PlatformSiteMetadataProvider>();
builder.Services.AddSingleton<ITextResourceProvider, EmbeddedTextResourceProvider>();

var app = builder.Build();

// Outermost, so an unexpected error on any path is a 500 problem without exception text.
app.UseExceptionHandler();

// Then tenant resolution, so API and SPA paths alike need a known tenant host.
app.UseTenantResolution();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthEndpoints();
app.MapPresentationEndpoints();
app.MapRecordEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
