using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Axis.Api.Authorization;
using Axis.Api.Endpoints;
using Axis.Api.HealthChecks;
using Axis.Api.Infrastructure;
using Axis.Shared.Infrastructure.Observability;
using Axis.Api.Middleware;
using Axis.DataModeling.Application.Commands.CreateModel;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Persistence;
using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.Identity.Application.Commands.RegisterOrganization;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Shared.Application.Behaviors;
using Axis.Shared.Application.Identity;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
using Axis.WorkflowEngine.Application.Commands.CancelExecution;
using Axis.WorkflowEngine.Infrastructure.Extensions;
using FluentValidation;
using JasperFx.Resources;
using MediatR;
using Wolverine.Persistence.Durability;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.DataModeling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using OpenIddict.Server.AspNetCore;
using Wolverine.Kafka;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
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

    // ── Observability (ADR-018) ─────────────────────────────────────────────
    builder.AddAxisOpenTelemetry();

    // ── Logging ────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With<TraceContextSerilogEnricher>());

    // ── Wolverine (messaging + per-module durable inbox/outbox per ADR-012) ─
    // Capture the live ConfigurationManager and read inside the lambda so
    // WebApplicationFactory.ConfigureAppConfiguration overrides applied later
    // in test setup are picked up by Wolverine.
    IConfiguration wolverineConfig = builder.Configuration;

    builder.Host.UseWolverine(opts =>
    {
        string identityConnectionString = wolverineConfig.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("ConnectionStrings:Identity is required");
        string dataModelingConnectionString = wolverineConfig.GetConnectionString("DataModeling")
            ?? throw new InvalidOperationException("ConnectionStrings:DataModeling is required");
        string workflowBuilderConnectionString = wolverineConfig.GetConnectionString("WorkflowBuilder")
            ?? throw new InvalidOperationException("ConnectionStrings:WorkflowBuilder is required");
        string formBuilderConnectionString = wolverineConfig.GetConnectionString("FormBuilder")
            ?? throw new InvalidOperationException("ConnectionStrings:FormBuilder is required");
        string workflowEngineConnectionString = wolverineConfig.GetConnectionString("WorkflowEngine")
            ?? throw new InvalidOperationException("ConnectionStrings:WorkflowEngine is required");

        string kafkaBrokers = wolverineConfig["Kafka:Brokers"]
            ?? throw new InvalidOperationException("Kafka:Brokers is required");

        string rabbitMqConnectionString = wolverineConfig.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is required");

        // Cross-cutting: Debug entry/exit traces + Error for unhandled exceptions on every handler.
        opts.Policies.AddMiddleware<HandlerLoggingMiddleware>();
        opts.UseEntityFrameworkCoreTransactions();

        // Main store: node/agent coordination in Identity DB (ADR-011 + ADR-012).
        opts.PersistMessagesWithPostgresql(identityConnectionString, "wolverine");

        // Per-module ancillary outbox — `wolverine` schema colocated with each module DB.
        opts.PersistMessagesWithPostgresql(identityConnectionString, "wolverine", MessageStoreRole.Ancillary)
            .Enroll<IdentityDbContext>();
        opts.PersistMessagesWithPostgresql(dataModelingConnectionString, "wolverine", MessageStoreRole.Ancillary)
            .Enroll<DataModelingDbContext>();
        opts.PersistMessagesWithPostgresql(workflowBuilderConnectionString, "wolverine", MessageStoreRole.Ancillary)
            .Enroll<WorkflowBuilderDbContext>();
        opts.PersistMessagesWithPostgresql(formBuilderConnectionString, "wolverine", MessageStoreRole.Ancillary)
            .Enroll<FormBuilderDbContext>();
        opts.PersistMessagesWithPostgresql(workflowEngineConnectionString, "wolverine", MessageStoreRole.Ancillary)
            .Enroll<WorkflowEngineDbContext>();

        // Cross-module event transport — `*Event`/`*Snapshot` messages.
        // Kafka also stores event-sourced aggregate logs. Per ADR-013 + the
        // message-suffix routing rule in ADR-025. No PublishMessage<>.ToKafkaTopic()
        // declarations yet — those land module-by-module. Payload format is
        // JSON for now; Avro + Schema Registry per ADR-019 ships next.
        //
        // Cross-module command/job/saga transport — `*Command`/`*Job`/`*SagaStep`.
        // Per ADR-024 + ADR-025. Work-queue semantics (ACK, requeue, DLX).
        // Wolverine saga state lives in Postgres `saga_state` per module.
        //
        // AutoProvision (both transports) is gated to non-production
        // environments — production should provision topics/exchanges through
        // a controlled pipeline so partitions/replication/retention/ACLs are
        // auditable rather than an app-startup side effect.
        bool autoProvisionAllowed =
            builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

        if (autoProvisionAllowed)
        {
            opts.UseKafka(kafkaBrokers).AutoProvision();
            opts.UseRabbitMq(new Uri(rabbitMqConnectionString)).AutoProvision();
        }
        else
        {
            opts.UseKafka(kafkaBrokers);
            opts.UseRabbitMq(new Uri(rabbitMqConnectionString));
        }

        // Infrastructure assemblies host Wolverine handlers (e.g. domain event consumers)
        // but are not the entry assembly — include them explicitly for handler discovery.
        opts.Discovery.IncludeAssembly(typeof(WorkflowEngineInfrastructureExtensions).Assembly);
        opts.Discovery.IncludeAssembly(typeof(FormBuilderInfrastructureExtensions).Assembly);
    });

    // Development + integration tests: auto-create each module's `wolverine` schema.
    // Production runs scripted SQL migrations per module DB (ADR-012).
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        builder.Services.AddResourceSetupOnStartup();

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
    builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
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

    // ── Dev bootstrap: apply Identity migrations before OpenIddictSeeder runs.
    //    Tenant module schemas are provisioned per-org by TenantSchemaProvisioner.
    if (app.Environment.IsDevelopment() && !EF.IsDesignTime)
    {
        using IServiceScope scope = app.Services.CreateScope();
        IdentityDbContext identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identityDb.Database.MigrateAsync();
    }

    app.UseAxisOpenTelemetry();
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
    app.MapFormTaskEndpoints();

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
