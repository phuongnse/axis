using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
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

// Under directory, the platform migrations and the first directory load run now, before the services are
// built, because sign-in registers one client per tenant. Under configuration, nothing about the platform
// schema is read.
ITenantCatalog tenants;
var directorySource = TenancySettings.Source(builder.Configuration) == TenancySource.Directory;
if (directorySource)
{
    // Read first, so a bad value stops the start even when the database is not there.
    var refreshInterval = TenancySettings.RefreshInterval(builder.Configuration);

    var platformSchemaConnectionString = builder.Configuration.GetConnectionString("PlatformSchema");
    if (string.IsNullOrWhiteSpace(platformSchemaConnectionString))
    {
        throw new InvalidOperationException("Connection string 'PlatformSchema' is required when Tenancy:Source is 'directory'.");
    }

    var readerRole = new NpgsqlConnectionStringBuilder(platformConnectionString).Username;
    if (string.IsNullOrWhiteSpace(readerRole))
    {
        throw new InvalidOperationException("Connection string 'Platform' must name a user when Tenancy:Source is 'directory'.");
    }

    if (string.Equals(readerRole, new NpgsqlConnectionStringBuilder(platformSchemaConnectionString).Username, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "Connection strings 'Platform' and 'PlatformSchema' name the same user. They must be different roles.");
    }

    // Activation needs schema connections, which the directory source does not give. Axis.Admin activates releases.
    var activateOnStartup = builder.Configuration.GetSection(StartupActivationOptions.SectionName).Get<string[]>() ?? [];
    if (activateOnStartup.Any(folder => !string.IsNullOrWhiteSpace(folder)))
    {
        throw new InvalidOperationException(
            "ActivateOnStartup must be empty when Tenancy:Source is 'directory'. Activate releases with Axis.Admin.");
    }

    var cipher = TenantSecretCipher.Load(builder.Configuration);
    await PlatformMigration.ApplyAsync(new PlatformMigrationOptions(platformSchemaConnectionString, readerRole), CancellationToken.None);
    var loaded = await DirectoryTenantCatalog.LoadAsync(platformConnectionString, cipher, CancellationToken.None);

    // The server serves a tenant only on its own schema version. Axis.Admin migrate brings a tenant there.
    var otherSchema = loaded.OnOtherSchemaVersion(TenantSchemaVersion.Current);

    // The tenants served at start count as checked: the release check runs the role check on them before the server listens.
    var directory = new TenantDirectoryCatalog(loaded.Withhold(otherSchema));
    tenants = directory;

    // Registered first, so it logs before the release check runs.
    builder.Services.AddSingleton(cipher);
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
    tenants = ConfigurationTenantCatalog.Load(builder.Configuration, builder.Environment);

    // Fails startup when a tenant has no schema connection string. Only startup activation uses it,
    // and the worker never uses it.
    builder.Services.AddSingleton(TenantSchemaConnections.Load(tenants));
}

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(platformConnectionString));
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

// Fails startup when a tenant has no complete Oidc block. Only the server signs people in.
var oidcTenants = new TenantSignInSettings(TenantOidcSettings.Load(tenants));

// The development, E2E and test servers run over http, where a Secure cookie never comes back.
var plainHttpAllowed = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var sessionCookiePolicy = plainHttpAllowed ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.TryAddSingleton(TimeProvider.System);

// Record writes, process starts, tasks and GET /api/me read the session. It is the default
// everywhere except endpoint paths, so a request that carries a session renews it. On endpoint
// paths the default is the caller's bearer token, so a session cookie there is never read or renewed.
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

// Studio is off unless folders and an author role are set, which is the Production default.
builder.Services.AddSingleton(StudioOptions.Load(builder.Configuration, builder.Environment.ContentRootPath));

builder.Services.AddScoped<TenantDatabase>();
builder.Services.AddSingleton<ActiveModelCache>();
builder.Services.AddScoped<ActiveApplicationResolver>();
builder.Services.AddSingleton<ISiteMetadataProvider, PlatformSiteMetadataProvider>();
builder.Services.AddSingleton<ITextResourceProvider, EmbeddedTextResourceProvider>();

var app = builder.Build();

// Outermost, so an unexpected error on any path is a 500 problem without exception text.
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
app.UseAuthentication();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthEndpoints();
app.MapPresentationEndpoints();
app.MapSiteEndpoints();
app.MapRecordEndpoints();
app.MapProcessEndpoints();
app.MapProcessInspectionEndpoints();
app.MapProcessOperatorEndpoints();
app.MapTaskEndpoints();
app.MapDataSourceEndpoints();
app.MapInboundEndpoints();
app.MapUserEndpoints();
app.MapStudioEndpoints();
app.MapAuthEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
