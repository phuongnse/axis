using Axis.ControlPlane;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Dns;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Health;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.SignUp;
using Axis.ControlPlane.Storage;
using Axis.ControlPlane.Subscriptions;
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

// The plan versions, built from the checked settings. The migration start then checks the versions that subscriptions pin.
builder.Services.AddSingleton(PlanCatalogue.Create(options.Plans));

// The proxies whose forwarded headers are trusted, by the platform's rules: the client address is then the real one, see
// docs/reference/control-plane.md#settings. Read before any database work too, so a bad value stops the start.
var forwardedHeaders = ForwardedHeadersSettings.Load(builder.Configuration);
builder.Services.PostConfigure<ForwardedHeadersOptions>(forwardedHeaders.Apply);

// The sign-up limits, kept in memory by each replica, see docs/reference/sign-up.md#bots-and-abuse.
builder.Services.AddSingleton<SignUpLimits>();

// Read next to the settings, so a kind without both languages stops the start.
builder.Services.AddSingleton(EmailTemplates.LoadEmbedded());

// The first hosted service: it migrates in StartingAsync, so a database that cannot be reached stops the start.
builder.Services.AddHostedService<ControlPlaneMigrationStart>();
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(options.ConnectionString!));

// After the migration start, whose StartingAsync runs before any hosted service starts. The runner claims only the kinds below.
builder.Services.AddSingleton(new JobRunnerOptions());
builder.Services.AddSingleton<IJobHandler, SendEmailJobHandler>();
builder.Services.AddSingleton<IJobHandler, SignUpConfirmJobHandler>();
builder.Services.AddSingleton<IJobHandler, SignUpExpiry>();
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

    // The jobs that write and remove a tenant's record. The options guarantee an IDnsRecords whenever the directory is set.
    builder.Services.AddSingleton<IJobHandler, WriteTenantRecordJobHandler>();
    builder.Services.AddSingleton<IJobHandler, RemoveTenantRecordJobHandler>();
    builder.Services.AddSingleton(sp => new SubdomainCheck(sp.GetRequiredKeyedService<NpgsqlDataSource>("directory"), sp.GetRequiredService<NpgsqlDataSource>(), options));
}

// The expiry of the pending sign-ups, queued every 5 minutes. It needs only the control plane's database.
builder.Services.AddSingleton(new RecurringJob(SignUpExpiry.JobKind, TimeSpan.FromMinutes(5)));

// The weekly refresh of the disposable mailbox domains, queued only when the list and its hash are both set. The options guarantee both.
if (options.SignUp.DisposableDomains.IsComplete)
{
    builder.Services.AddSingleton<IJobHandler, DisposableDomainRefresh>();
    builder.Services.AddSingleton(new RecurringJob(DisposableDomainRefresh.JobKind, TimeSpan.FromDays(7)));

    // Known lists are about 100 KB, so the cap only stops a wrong address from filling the replica's memory.
    builder.Services.AddHttpClient(DisposableDomainRefresh.HttpClientName, client => client.MaxResponseContentBufferSize = 10 * 1024 * 1024);
}

// The correction of the directory from the cells' tenant lists, queued every 5 minutes. It needs the directory and a cell to read.
if (!string.IsNullOrWhiteSpace(options.DirectoryConnectionString) && options.Cells.Count > 0)
{
    builder.Services.AddSingleton<IJobHandler, DirectoryCorrection>();
    builder.Services.AddSingleton(new RecurringJob(DirectoryCorrection.JobKind, TimeSpan.FromMinutes(5)));

    // The provisioning of a confirmed sign-up. It places the tenant in a cell, so it needs the directory and a cell to call.
    builder.Services.AddSingleton<CellPicker>();
    builder.Services.AddSingleton<IJobHandler, ProvisionTenantJobHandler>();

    // The clock of the subscriptions, queued every hour. It finds each tenant's cell in the directory and calls it.
    builder.Services.AddSingleton<IJobHandler, SubscriptionClock>();
    builder.Services.AddSingleton(new RecurringJob(SubscriptionClock.JobKind, TimeSpan.FromHours(1)));

    // The write of a subscription's quotas to its cell, and its sync over every subscription, queued every 24 hours. Both find the tenant's cell in the directory and call it.
    builder.Services.AddSingleton<IJobHandler, SubscriptionQuotas>();
    builder.Services.AddSingleton<IJobHandler, QuotaSync>();
    builder.Services.AddSingleton(new RecurringJob(QuotaSync.JobKind, TimeSpan.FromDays(1)));
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

// Before the routes, so the sign-up limits see the client address. The built-in middleware skips the proxy check when a
// request has no client address, so such a request is kept out of it. Off when no proxy is named.
if (!forwardedHeaders.IsEmpty)
{
    app.UseWhen(context => context.Connection.RemoteIpAddress is not null, branch => branch.UseForwardedHeaders());
}

app.UseStaticFiles();

app.MapHealthEndpoints();
app.MapSignUpEndpoints();

// The sign-up page and its verify page, built by web/vite.sign-up.config.ts into the web root. The bundle picks the page by its path.
app.MapFallbackToFile("/sign-up", "sign-up.html");
app.MapFallbackToFile("/sign-up/verify", "sign-up.html");

app.Run();

/// <summary>Entry point, exposed for tests.</summary>
public partial class Program;
