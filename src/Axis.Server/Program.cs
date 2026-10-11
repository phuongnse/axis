using Axis.Presentation.Sites;
using Axis.Presentation.Texts;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.DataSources;
using Axis.Server.Health;
using Axis.Server.Http;
using Axis.Server.Presentation;
using Axis.Server.Processes;
using Axis.Server.Records;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

// Fails startup when a tenant has no complete Oidc block. Only the server signs people in.
var oidcTenants = TenantOidcSettings.Load(builder.Configuration);

// Fails startup when test users are configured outside Development and Testing. The directory is
// registered in every environment, because GET /api/me reads it.
var testUsers = TestUserDirectory.Load(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(testUsers);

// The development, E2E and test servers run over http, where a Secure cookie never comes back.
var plainHttpAllowed = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var sessionCookiePolicy = plainHttpAllowed ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.TryAddSingleton(TimeProvider.System);

// Record writes, process starts and tasks read the session first. The test-user cookie stays the
// default scheme while test users are enabled, so a signed-in test user still counts without one.
var authentication = builder.Services.AddAuthentication(
    testUsers.Enabled ? CookieAuthenticationDefaults.AuthenticationScheme : Session.Scheme);
if (testUsers.Enabled)
{
    authentication.AddCookie(options =>
    {
        options.Cookie.Name = "Axis.TestUser";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // The development and E2E servers run over http.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // An API answers with a status code, never a redirect to a sign-in page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
}

authentication
    .AddSession(sessionCookiePolicy)
    .AddTenantSignIn(oidcTenants, sessionCookiePolicy, allowHttp: plainHttpAllowed);

// Off unless folders are listed, which is the Production default.
var startupFolders = builder.Configuration.GetSection(StartupActivationOptions.SectionName).Get<string[]>() ?? [];
if (startupFolders.Length > 0)
{
    builder.Services.AddSingleton(new StartupActivationOptions(
        [.. startupFolders.Select(folder => Path.GetFullPath(folder, builder.Environment.ContentRootPath))]));
    builder.Services.AddHostedService<StartupActivation>();
}

builder.Services.AddScoped<TenantDatabase>();
builder.Services.AddSingleton<ActiveModelCache>();
builder.Services.AddScoped<ActiveApplicationResolver>();
builder.Services.AddSingleton<ISiteMetadataProvider, PlatformSiteMetadataProvider>();
builder.Services.AddSingleton<ITextResourceProvider, EmbeddedTextResourceProvider>();

var app = builder.Build();

// Outermost, so an unexpected error on any path is a 500 problem without exception text.
app.UseExceptionHandler();

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
app.MapTaskEndpoints();
app.MapDataSourceEndpoints();
app.MapUserEndpoints();
app.MapAuthEndpoints();

// The sign-in endpoints exist only in Development and Testing. The test user list answers 404 elsewhere.
if (testUsers.Enabled)
{
    app.MapTestUserEndpoints();
}

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point, exposed for integration tests.</summary>
public partial class Program;
