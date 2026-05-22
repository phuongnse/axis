using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Axis.Api.Authorization;
using Axis.Api.Endpoints;
using Axis.Api.HealthChecks;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.DataModeling.Application.Commands.CreateModel;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.Identity.Application.Commands.RegisterOrganization;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Shared.Application.Behaviors;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
using Axis.WorkflowEngine.Application.Commands.CancelExecution;
using Axis.WorkflowEngine.Infrastructure.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Scalar.AspNetCore;
using Serilog;
using StackExchange.Redis;
using Axis.Shared.Infrastructure.Wolverine;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // ── Logging ────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ── Wolverine (messaging + domain event outbox) ────────────────────────
    builder.Host.UseWolverine(opts =>
    {
        // Cross-cutting: Debug entry/exit traces + Error for unhandled exceptions on every handler.
        opts.Policies.AddMiddleware<HandlerLoggingMiddleware>();
        opts.UseEntityFrameworkCoreTransactions();
        // Infrastructure assemblies host Wolverine handlers (e.g. domain event consumers)
        // but are not the entry assembly — include them explicitly for handler discovery.
        opts.Discovery.IncludeAssembly(typeof(WorkflowEngineInfrastructureExtensions).Assembly);
        opts.Discovery.IncludeAssembly(typeof(FormBuilderInfrastructureExtensions).Assembly);
    });

    // ── MediatR + validation pipeline ─────────────────────────────────────
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblies(
            typeof(RegisterOrganizationCommand).Assembly,
            typeof(CreateModelCommand).Assembly,
            typeof(CreateWorkflowCommand).Assembly,
            typeof(CreateFormCommand).Assembly,
            typeof(CancelExecutionCommand).Assembly
        );
        cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    builder.Services.AddValidatorsFromAssemblies([
        typeof(RegisterOrganizationCommand).Assembly,
        typeof(CreateModelCommand).Assembly,
        typeof(CreateWorkflowCommand).Assembly,
        typeof(CreateFormCommand).Assembly,
        typeof(CancelExecutionCommand).Assembly,
    ]);

    // ── Authentication & OpenIddict ────────────────────────────────────────
    // Cookie scheme: short-lived session used only during the PKCE authorize flow
    // (POST /connect/login → GET /connect/authorize). Not used for API calls.
    // OpenIddict validation scheme: validates JWT access tokens on every API request.
    builder.Services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opts =>
        {
            opts.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            opts.SlidingExpiration = false;
            // Challenge returns 302 to /connect/login — only hit by the authorize endpoint.
            // API endpoints use OpenIddict validation which challenges with 401 Bearer.
            opts.LoginPath = "/connect/login";
        });

    builder.Services.AddOpenIddict()
        // Core: EF Core token/application/scope stores (registered in IdentityInfrastructureExtensions)
        // NOTE: AddCore is called in AddIdentityInfrastructure; we only add Server and Validation here.

        // Server: the in-process OAuth2/OIDC authorization server
        .AddServer(opts =>
        {
            opts.SetAuthorizationEndpointUris("/connect/authorize")
                .SetTokenEndpointUris("/connect/token");

            // Register supported scopes so OpenIddict validates them during authorize requests.
            // Custom scopes (e.g. "permissions") are also seeded as OpenIddictScope entities by
            // OpenIddictSeeder so resource servers can discover them via introspection.
            opts.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, "permissions");

            // Authorization Code + PKCE — SPA flow
            opts.AllowAuthorizationCodeFlow()
                .RequireProofKeyForCodeExchange();

            // Client Credentials — M2M / external system integrations
            opts.AllowClientCredentialsFlow();

            // Refresh Token — silent session renewal
            opts.AllowRefreshTokenFlow();

            // Refresh tokens stored as opaque references in the DB → easy revocation
            opts.UseReferenceRefreshTokens();

            // Ephemeral keys for development. Production should use
            // .AddEncryptionCertificate() / .AddSigningCertificate() from Azure Key Vault.
            opts.AddEphemeralEncryptionKey()
                .AddEphemeralSigningKey();

            opts.UseAspNetCore()
                // Passthrough: our endpoint handlers call Results.SignIn to complete the flow
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough();

            // Move refresh token out of response body and into an httpOnly cookie
            opts.AddEventHandler<ApplyTokenResponseContext>(b =>
                b.UseSingletonHandler<ApplyRefreshTokenCookieHandler>());

            // Read refresh token from cookie instead of request body on refresh
            opts.AddEventHandler<ExtractTokenRequestContext>(b =>
                b.UseSingletonHandler<ExtractRefreshTokenFromCookieHandler>());
        })

        // Validation: validates JWT access tokens on incoming API requests (same process — no network call)
        .AddValidation(opts =>
        {
            opts.UseLocalServer();
            opts.UseAspNetCore();
        });

    // ── Authorization ──────────────────────────────────────────────────────
    builder.Services.AddAuthorization();
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

    // ── Rate limiting ──────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(opts =>
    {
        opts.AddFixedWindowLimiter("auth", cfg =>
        {
            cfg.PermitLimit = 10;
            cfg.Window = TimeSpan.FromMinutes(1);
            cfg.QueueLimit = 0;
        });
        opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    // ── Module infrastructure ──────────────────────────────────────────────
    IConfiguration cfg = builder.Configuration;
    builder.Services.AddIdentityInfrastructure(cfg);
    builder.Services.AddDataModelingInfrastructure(cfg);
    builder.Services.AddWorkflowBuilderInfrastructure(cfg);
    builder.Services.AddFormBuilderInfrastructure(cfg);
    builder.Services.AddWorkflowEngineInfrastructure(cfg);

    // ── Redis ──────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(
            builder.Configuration["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Redis:ConnectionString is required")));
    builder.Services.AddSingleton<IJtiBlacklist, RedisJtiBlacklist>();

    // ── API services ───────────────────────────────────────────────────────
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<CurrentUser>();
    builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

    // ── CORS ───────────────────────────────────────────────────────────────
    string[] allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [];

    builder.Services.AddCors(opts => opts.AddPolicy("SpaOrigin", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

    // ── JSON ───────────────────────────────────────────────────────────────
    builder.Services.ConfigureHttpJsonOptions(opts =>
    {
        opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        opts.SerializerOptions.PropertyNameCaseInsensitive = true;
        opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        opts.SerializerOptions.Converters.Add(new AddFieldRequestConverter());
        opts.SerializerOptions.Converters.Add(new UpdateFieldRequestConverter());
        opts.SerializerOptions.Converters.Add(new AddDataClassFieldRequestConverter());
        opts.SerializerOptions.Converters.Add(new AddFormFieldRequestConverter());
    });

    // ── OpenAPI ────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(opts =>
    {
        opts.SwaggerDoc("v1", new OpenApiInfo { Title = "Axis API", Version = "v1" });
        opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        });
        opts.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                },
                []
            },
        });
    });

    // ── Health checks ──────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"])
        .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

    // ── Build ──────────────────────────────────────────────────────────────
    WebApplication app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ValidationExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors("SpaOrigin");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── OpenAPI / Scalar (dev + staging only) ─────────────────────────────
    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        app.UseSwagger();
        app.MapScalarApiReference(options =>
        {
            options.WithOpenApiRoutePattern("/swagger/v1/swagger.json");
            options.Title = "Axis API";
            options.Theme = ScalarTheme.Moon;
        });
    }

    // ── Health endpoints (anonymous, no rate limiting) ─────────────────────
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
    });

    // ── OpenIddict connect endpoints ───────────────────────────────────────
    app.MapConnectEndpoints();

    // ── Module endpoints ───────────────────────────────────────────────────
    app.MapAuthEndpoints();
    app.MapOrganizationEndpoints();
    app.MapInvitationEndpoints();
    app.MapUserEndpoints();
    app.MapRoleEndpoints();
    app.MapModelEndpoints();
    app.MapDataClassEndpoints();
    app.MapRecordEndpoints();
    app.MapWorkflowEndpoints();
    app.MapExecutionEndpoints();
    app.MapFormEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Needed for WebApplicationFactory in integration tests
public partial class Program;
