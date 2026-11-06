using Axis.ControlPlane;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Dns;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Health;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.SignUp;
using Axis.ControlPlane.Storage;
using Axis.ControlPlane.Telemetry;
using Axis.Server.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

// The clock of the job schedule. AddCellClients registers it too, whichever comes first.
builder.Services.TryAddSingleton(TimeProvider.System);

// The clients of the region's cells. With no cell listed they hold nothing and the service starts as before.
builder.Services.AddCellClients(options);

// The DNS adapter that sets and removes a tenant's CNAME record, by the provider named. Without one the service starts as before.
if (string.Equals(options.Dns.Provider, DnsProviders.InMemory, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<InMemoryDnsRecords>();
    builder.Services.AddSingleton<IDnsRecords>(services => services.GetRequiredService<InMemoryDnsRecords>());
}
else if (string.Equals(options.Dns.Provider, DnsProviders.Rfc2136, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDnsRecords>(services => new Rfc2136DnsRecords(options.Dns, services.GetRequiredService<TimeProvider>()));
}

// The region directory's own database, keyed so that it does not clash with the control plane's. Without it the service starts as before.
if (!string.IsNullOrWhiteSpace(options.DirectoryConnectionString))
{
    builder.Services.AddKeyedSingleton("directory", (_, _) => NpgsqlDataSource.Create(options.DirectoryConnectionString));
    builder.Services.AddSingleton(sp => new SubdomainCheck(sp.GetRequiredKeyedService<NpgsqlDataSource>("directory"), options));
}

// The correction of the directory from the cells' tenant lists, queued every 5 minutes. It needs the directory and a cell to read.
if (!string.IsNullOrWhiteSpace(options.DirectoryConnectionString) && options.Cells.Count > 0)
{
    builder.Services.AddSingleton<IJobHandler, DirectoryCorrection>();
    builder.Services.AddSingleton(new RecurringJob(DirectoryCorrection.JobKind, TimeSpan.FromMinutes(5)));
}

// The schedule that queues the recurring jobs above. It is idle when there are none.
builder.Services.AddHostedService<JobSchedule>();

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

app.UseStaticFiles();

app.MapHealthEndpoints();
app.MapSignUpEndpoints();

// The sign-up page, built by web/vite.sign-up.config.ts into the web root.
app.MapFallbackToFile("/sign-up", "sign-up.html");

app.Run();

/// <summary>Entry point, exposed for tests.</summary>
public partial class Program;
