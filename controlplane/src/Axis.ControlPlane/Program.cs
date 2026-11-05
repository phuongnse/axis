using Axis.ControlPlane;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Health;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Storage;
using Axis.ControlPlane.Telemetry;
using Axis.Server.Http;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// A file in /run/secrets, named as a setting in its environment variable form, wins over the variable.
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

// Read before any database work, so a missing or wrong setting stops the start.
var options = ControlPlaneOptions.Load(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(options);

// Read next to the settings, so a kind without both languages stops the start.
builder.Services.AddSingleton(EmailTemplates.LoadEmbedded());

// The first hosted service: it migrates in StartingAsync, so a database that cannot be reached stops the start.
builder.Services.AddHostedService<ControlPlaneMigrationStart>();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(options.ConnectionString!));

// After the migration start, whose StartingAsync runs before any hosted service starts. The runner claims only the kinds below.
builder.Services.AddSingleton(new JobRunnerOptions());
builder.Services.AddSingleton<IJobHandler, SendEmailJobHandler>();
builder.Services.AddHostedService<JobRunner>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadyTag])
    .AddCheck<MailRelayHealthCheck>("mail-relay", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddProblemDetails();
builder.Services.AddControlPlaneTelemetry(builder.Configuration, builder.Environment);

var app = builder.Build();

// First, so every answer carries the security headers, as the platform's hosts do.
app.UseSecurityHeaders(strictTransportSecurity: !app.Environment.IsDevelopment());

// Outermost after the security headers, so an unexpected error on any path is a 500 problem without exception text.
app.UseExceptionHandler();

app.MapHealthEndpoints();

app.Run();

/// <summary>Entry point, exposed for tests.</summary>
public partial class Program;
