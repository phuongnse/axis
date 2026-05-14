using System.Text;
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
using Axis.DataModeling.Infrastructure.Persistence;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
using StackExchange.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;

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
    // Registers IMessageBus in DI. Domain events raised by aggregates are
    // dispatched here after SaveChangesAsync via UnitOfWork.
    // Durable PostgreSQL outbox will be added once the Wolverine persistence
    // schema strategy is decided (tracked in PROGRESS.md).
    builder.Host.UseWolverine(opts =>
    {
        opts.UseEntityFrameworkCoreTransactions();
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
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    builder.Services.AddValidatorsFromAssemblies([
        typeof(RegisterOrganizationCommand).Assembly,
        typeof(CreateModelCommand).Assembly,
        typeof(CreateWorkflowCommand).Assembly,
        typeof(CreateFormCommand).Assembly,
        typeof(CancelExecutionCommand).Assembly,
    ]);

    // ── JWT Authentication ─────────────────────────────────────────────────
    // NOTE: This will be replaced by OpenIddict (Authorization Code + PKCE)
    // once the Identity API layer is refactored. See ADR-004 in TECH_STACK.md.
    string jwtKey = builder.Configuration["Jwt:SecretKey"]
        ?? throw new InvalidOperationException("Jwt:SecretKey is required");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    string? jti = context.Principal?.FindFirst("jti")?.Value;
                    if (jti is not null)
                    {
                        IJtiBlacklist blacklist = context.HttpContext.RequestServices
                            .GetRequiredService<IJtiBlacklist>();
                        if (await blacklist.IsBlacklistedAsync(jti))
                            context.Fail("Token has been revoked.");
                    }
                },
                OnChallenge = async context =>
                {
                    if (!context.Handled)
                    {
                        context.HandleResponse();
                        await Results.Problem(
                            title: "Unauthorized",
                            detail: "Authentication is required to access this resource.",
                            statusCode: StatusCodes.Status401Unauthorized)
                            .ExecuteAsync(context.HttpContext);
                    }
                },
                OnForbidden = async context =>
                {
                    await Results.Problem(
                        title: "Forbidden",
                        detail: "You do not have permission to perform this action.",
                        statusCode: StatusCodes.Status403Forbidden)
                        .ExecuteAsync(context.HttpContext);
                },
            };
        });

    // ── Authorization ──────────────────────────────────────────────────────
    builder.Services.AddAuthorization();
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

    // ── Rate limiting ──────────────────────────────────────────────────────
    // Applied to auth endpoints only. Authenticated API endpoints are not rate-limited by default.
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
    builder.Services.AddWorkflowBuilderInfrastructure(cfg.GetConnectionString("WorkflowBuilder")!);
    builder.Services.AddFormBuilderInfrastructure(cfg.GetConnectionString("FormBuilder")!);
    builder.Services.AddWorkflowEngineInfrastructure(cfg.GetConnectionString("WorkflowEngine")!);

    // ── Redis ──────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(
            builder.Configuration["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Redis:ConnectionString is required")));
    builder.Services.AddSingleton<IJtiBlacklist, RedisJtiBlacklist>();

    // ── API services ───────────────────────────────────────────────────────
    builder.Services.AddScoped<ITokenService, JwtTokenService>();
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

    // ── Module endpoints ───────────────────────────────────────────────────
    app.MapAuthEndpoints();
    app.MapOrganizationEndpoints();
    app.MapInvitationEndpoints();
    app.MapUserEndpoints();
    app.MapRoleEndpoints();
    app.MapModelEndpoints();
    app.MapDataClassEndpoints();
    app.MapRecordEndpoints();

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
