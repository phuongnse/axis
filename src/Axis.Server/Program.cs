using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Templates;
using Axis.Data;
using Axis.Presentation.Sites;
using Axis.Presentation.Texts;
using Axis.Processes.Storage;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.DataSources;
using Axis.Server.Endpoints;
using Axis.Server.Health;
using Axis.Server.Http;
using Axis.Server.Presentation;
using Axis.Server.Processes;
using Axis.Server.Records;
using Axis.Server.Studio;
using Axis.Server.Telemetry;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// A file in /run/secrets, named as a setting in its environment variable form, wins over the variable.
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

var platformConnectionString = builder.Configuration.GetConnectionString("Platform");
if (string.IsNullOrWhiteSpace(platformConnectionString))
{
    throw new InvalidOperationException("Connection string 'Platform' is not configured.");
}

// Read before any database work, so a bad value stops the start.
var forwardedHeaders = ForwardedHeadersSettings.Load(builder.Configuration);
builder.Services.PostConfigure<ForwardedHeadersOptions>(forwardedHeaders.Apply);

// Read before any database work too, so a bad value stops the start.
var rateLimits = RateLimitSettings.Load(builder.Configuration);
builder.Services.AddSingleton(rateLimits);
builder.Services.AddSingleton<RateLimitRefusals>();
builder.Services.AddRateLimiter(_ => { });
builder.Services.AddOptions<RateLimiterOptions>().Configure<RateLimitSettings, RateLimitRefusals>(RateLimits.Configure);

// Both sources need the schema connection, a reader role and the key, so a missing setting stops the start
// before any database work.
var platformSchemaConnectionString = builder.Configuration.GetConnectionString("PlatformSchema");
if (string.IsNullOrWhiteSpace(platformSchemaConnectionString))
{
    throw new InvalidOperationException("Connection string 'PlatformSchema' is not configured.");
}

var readerRole = new NpgsqlConnectionStringBuilder(platformConnectionString).Username;
if (string.IsNullOrWhiteSpace(readerRole))
{
    throw new InvalidOperationException("Connection string 'Platform' must name a user.");
}

if (string.Equals(readerRole, new NpgsqlConnectionStringBuilder(platformSchemaConnectionString).Username, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Connection strings 'Platform' and 'PlatformSchema' name the same user. They must be different roles.");
}

var cipher = TenantSecretCipher.Load(builder.Configuration);
builder.Services.AddSingleton(cipher);
var platformMigration = new PlatformMigrationOptions(platformSchemaConnectionString, readerRole);

// Under directory, the platform migrations and the first directory load run now, before the services are
// built, because sign-in registers one client per tenant. Under configuration, the migrations run as a hosted
// service. Either way the platform migrations run before anything else reads the platform tables.
ITenantCatalog tenants;
var directorySource = TenancySettings.Source(builder.Configuration) == TenancySource.Directory;
if (directorySource)
{
    // Read first, so a bad value stops the start even when the database is not there.
    var refreshInterval = TenancySettings.RefreshInterval(builder.Configuration);

    // Activation needs schema connections, which the directory source does not give. Axis.Admin activates releases.
    var activateOnStartup = builder.Configuration.GetSection(StartupActivationOptions.SectionName).Get<string[]>() ?? [];
    if (activateOnStartup.Any(folder => !string.IsNullOrWhiteSpace(folder)))
    {
        throw new InvalidOperationException(
            "ActivateOnStartup must be empty when Tenancy:Source is 'directory'. Activate releases with Axis.Admin.");
    }

    await PlatformMigration.ApplyAsync(platformMigration, CancellationToken.None);
    var loaded = await DirectoryTenantCatalog.LoadAsync(platformConnectionString, cipher, CancellationToken.None);

    // The server serves a tenant only on its own schema version. Axis.Admin migrate brings a tenant there.
    var otherSchema = loaded.OnOtherSchemaVersion(TenantSchemaVersion.Current);

    // The tenants served at start count as checked: the release check runs the role check on them before the server listens.
    var directory = new TenantDirectoryCatalog(loaded.Withhold(otherSchema));
    tenants = directory;

    // Registered first, so it logs before the release check runs.
    builder.Services.AddSingleton(directory);
    builder.Services.AddSingleton<TenantDirectoryRefreshStatus>();
    builder.Services.AddSingleton(new TenantDirectoryRefreshOptions(platformConnectionString, refreshInterval));
    builder.Services.AddSingleton<TenantDirectoryRefresh>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<TenantDirectoryRefresh>());
    builder.Services.AddSingleton(new TenantDirectoryStart(
        readerRole,
        tenants.Tenants.Count,
        builder.Configuration.GetSection(TenantOptions.SectionName).Exists(),
        otherSchema));
    builder.Services.AddHostedService<TenantDirectoryStartLog>();
}
else
{
    // The first hosted service. It migrates in StartingAsync, as StartupActivation does, and the host runs those in
    // registration order, so it runs before StartupActivation. Nothing above registers a hosted service.
    builder.Services.AddSingleton(platformMigration);
    builder.Services.AddHostedService<PlatformMigrationStart>();

    tenants = ConfigurationTenantCatalog.Load(builder.Configuration, builder.Environment);

    // Fails startup when a tenant has no schema connection string. Only startup activation uses it,
    // and the worker never uses it.
    builder.Services.AddSingleton(TenantSchemaConnections.Load(tenants));
}

// Every replica reads the same keys, encrypted with Platform:SecretKey. The start check decrypts them all, after
// the platform migrations and before startup activation. The application name is fixed, because ASP.NET otherwise
// isolates keys by content root path, and replicas at different paths would refuse each other's cookies.
builder.Services.AddSingleton<PlatformKeyRing>();
builder.Services.AddDataProtection().SetApplicationName("Axis");
builder.Services.AddOptions<KeyManagementOptions>().Configure<PlatformKeyRing>((options, ring) => options.XmlRepository = ring);
builder.Services.AddHostedService<PlatformKeyRingStart>();

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(platformConnectionString));
builder.Services.AddSingleton<IStudioWriteLock, PlatformStudioWriteLock>();
builder.Services.AddSingleton<IStudioFileWriter, StudioFileWriter>();
builder.Services.AddSingleton<TenantDatabaseGauge>();

// Under directory, the server serves its tenants from the last good read while the platform database is down.
// Readiness is Degraded then, not Unhealthy, so a load balancer keeps the server in service.
var health = builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(
        "database",
        failureStatus: directorySource ? HealthStatus.Degraded : HealthStatus.Unhealthy,
        tags: [HealthEndpoints.ReadyTag])
    .AddCheck<StartupActivationHealthCheck>("activation", tags: [HealthEndpoints.ReadyTag])
    .AddCheck<TenantDatabasesHealthCheck>("tenant-databases", failureStatus: HealthStatus.Degraded, tags: [HealthEndpoints.ReadyTag]);
if (directorySource)
{
    health.AddCheck<TenantDirectoryHealthCheck>("tenant-directory", failureStatus: HealthStatus.Degraded, tags: [HealthEndpoints.ReadyTag]);
}

// Every problem details answer carries the request's trace id.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemTraceId.Apply);
builder.Services.AddServerTelemetry(builder.Configuration);
builder.Services.AddTenancy(tenants);
builder.Services.AddExtensionPackages(builder.Configuration, builder.Environment);
builder.Services.AddModules(builder.Configuration, builder.Environment);
builder.Services.AddTemplates(builder.Configuration, builder.Environment);

// Fails startup when a tenant has no complete Oidc block. Only the server signs people in.
var oidcTenants = new TenantSignInSettings(TenantOidcSettings.Load(tenants));

// The development, E2E and test servers run over http, where a Secure cookie never comes back.
var plainHttpAllowed = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var sessionCookiePolicy = plainHttpAllowed ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.TryAddSingleton(TimeProvider.System);

// Record writes, process starts, tasks and GET /api/me read the session. It is the default
// everywhere except endpoint paths and record routes with a bearer token, so a request that carries a
// session renews it. On endpoint paths the default is the caller's bearer token, and on a record route
// that has one it is the service account's, so a session cookie there is never read or renewed.
builder.Services.AddAuthentication(EndpointCaller.RequestScheme)
    .AddSession(sessionCookiePolicy)
    .AddTenantSignIn(oidcTenants, sessionCookiePolicy, allowHttp: plainHttpAllowed)
    .AddEndpointCallers(oidcTenants, allowHttp: plainHttpAllowed);

// Off unless folders are listed, which is the Production default.
var startupFolders = builder.Configuration.GetSection(StartupActivationOptions.SectionName).Get<string[]>() ?? [];
builder.Services.AddSingleton(new StartupActivationStatus(finished: startupFolders.Length == 0));
if (startupFolders.Length > 0)
{
    builder.Services.AddSingleton(new StartupActivationOptions(
        [.. startupFolders.Select(folder => Path.GetFullPath(folder, builder.Environment.ContentRootPath))],
        builder.Configuration.GetValue("Activation:InlineRecomputeLimit", ReleaseActivator.DefaultInlineRecomputeLimit)));
    builder.Services.AddHostedService<StartupActivation>();
}

// Registered after StartupActivation, so a release activated at this start is the one checked.
builder.Services.AddHostedService<StartupReleaseCheck>();

// Studio is off unless folders or an applications path, and an author role, are set, which is the Production default.
builder.Services.AddSingleton(QueryTimeoutSettings.Load(builder.Configuration));
builder.Services.AddSingleton(StudioOptions.Load(builder.Configuration, builder.Environment.ContentRootPath));

builder.Services.AddScoped<TenantDatabase>();
builder.Services.AddSingleton<ActiveModelCache>();
builder.Services.AddScoped<ActiveApplicationResolver>();
builder.Services.AddSingleton<ISiteMetadataProvider, PlatformSiteMetadataProvider>();
builder.Services.AddSingleton<ITextResourceProvider, EmbeddedTextResourceProvider>();

var app = builder.Build();

// First, so every answer carries the security headers. They are written when the response starts, after the
// forwarded headers have set the scheme and after any error answer is written.
app.UseSecurityHeaders(strictTransportSecurity: !app.Environment.IsDevelopment());

// Outermost after the security headers, so an unexpected error on any path is a 500 problem without exception text.
app.UseExceptionHandler();

// Before tenant resolution, so the forwarded host picks the tenant. The built-in middleware skips the proxy check
// when a request has no client address, so such a request is kept out of it. Off when no proxy is named.
if (!forwardedHeaders.IsEmpty)
{
    app.UseWhen(context => context.Connection.RemoteIpAddress is not null, branch => branch.UseForwardedHeaders());
}

// Then tenant resolution, so API and SPA paths alike need a known tenant host, and a session is
// checked against the resolved tenant.
app.UseTenantResolution();

// Before the session, so a refused write does no work and renews no cookie.
app.UseCsrfHeader();

// The OIDC handler answers the callback inside authentication, so a limiter after it never sees the callback. The
// callback is counted here, and only here.
app.UseWhen(RateLimits.IsCallback, branch => branch.UseRateLimiter());
app.UseAuthentication();

// After authentication, so the caller is known and a refused request still gets its 401 or 403 first.
app.UseWhen(context => !RateLimits.IsCallback(context), branch => branch.UseRateLimiter());

app.UseDefaultFiles();

// Vite writes the hashed files under /assets/, so they never change under their name. index.html and the other
// files keep revalidating by ETag.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
        {
            context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});

// Open routes have no limit. Every other route has one, and a route that sets its own partition overrides its group's.
var open = app.MapGroup(string.Empty).WithoutRateLimit();
open.MapHealthEndpoints();
open.MapPresentationEndpoints();

var sessions = app.MapGroup(string.Empty).WithRateLimit(Partition.Session);
sessions.MapSiteEndpoints();
sessions.MapRecordEndpoints();
sessions.MapFormStateEndpoints();
sessions.MapProcessEndpoints();
sessions.MapProcessInspectionEndpoints();
sessions.MapProcessOperatorEndpoints();
sessions.MapTaskEndpoints();
sessions.MapDataSourceEndpoints();
sessions.MapUserEndpoints();
sessions.MapUserPickerEndpoints();
sessions.MapStudioEndpoints();
sessions.MapAuthEndpoints();

app.MapGroup(string.Empty).WithBearerRateLimit().MapInboundEndpoints();

app.MapFallbackToFile("index.html").WithoutRateLimit();

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
