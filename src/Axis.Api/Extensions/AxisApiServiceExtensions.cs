using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Axis.Api.Authorization;
using Axis.Api.HealthChecks;
using Axis.Api.Infrastructure;
using Axis.DataModeling.Application.Commands.CreateModel;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.Identity.Application.Commands.RegisterOrganization;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Shared.Application.Behaviors;
using Axis.Shared.Application.Identity;
using Axis.Shared.Application.Tenancy;
using Axis.Shared.Infrastructure.Observability;
using Axis.Shared.Infrastructure.Tenancy;
using Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
using Axis.WorkflowEngine.Application.Commands.CancelExecution;
using Axis.WorkflowEngine.Infrastructure.Extensions;
using FluentValidation;
using JasperFx.Resources;
using MediatR;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Serilog;
using StackExchange.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Axis.Api.Extensions;

internal static class AxisApiServiceExtensions
{
    private const string AuthRateLimiterPolicy = "auth";

    public static WebApplicationBuilder AddAxisApiServices(this WebApplicationBuilder builder)
    {
        builder.AddAxisOpenTelemetry();
        builder.AddAxisLogging();
        builder.AddAxisMessaging();

        if (builder.Environment.IsDevelopmentOrTesting())
            builder.Services.AddResourceSetupOnStartup();

        builder.Services.AddAxisMediatR();
        builder.Services.AddAxisAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddAxisAuthorization();
        builder.Services.AddAxisForwardedHeaders();
        builder.Services.AddAxisRateLimiting(builder.Configuration, builder.Environment);
        builder.Services.AddAxisModules(builder.Configuration);
        builder.Services.AddAxisGrpcClients(builder.Configuration, builder.Environment);
        builder.Services.AddAxisRedis(builder.Configuration);
        builder.Services.AddAxisRequestContext();
        builder.Services.AddAxisCors(builder.Configuration);
        builder.Services.AddAxisJson();
        builder.Services.AddAxisOpenApi();
        builder.Services.AddAxisHealthChecks();

        return builder;
    }

    private static void AddAxisLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog(
            (ctx, services, config) => config
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.With<TraceContextSerilogEnricher>(),
            writeToProviders: true);
    }

    private static void AddAxisMediatR(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                typeof(RegisterOrganizationCommand).Assembly,
                typeof(CreateModelCommand).Assembly,
                typeof(CreateWorkflowCommand).Assembly,
                typeof(CreateFormCommand).Assembly,
                typeof(CancelExecutionCommand).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssemblies([
            typeof(RegisterOrganizationCommand).Assembly,
            typeof(CreateModelCommand).Assembly,
            typeof(CreateWorkflowCommand).Assembly,
            typeof(CreateFormCommand).Assembly,
            typeof(CancelExecutionCommand).Assembly,
        ]);
    }

    private static void AddAxisAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opts =>
            {
                opts.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                opts.SlidingExpiration = false;
                opts.LoginPath = "/connect/login";
            });

        services.AddOpenIddict()
            .AddServer(opts =>
            {
                opts.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token");

                opts.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, "permissions");

                opts.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();
                opts.AllowClientCredentialsFlow();
                opts.AllowRefreshTokenFlow();
                opts.UseReferenceRefreshTokens();

                opts.SetAccessTokenLifetime(ReadPositiveTimeSpan(
                    configuration,
                    "Jwt:AccessTokenTtlMinutes",
                    defaultValue: 15,
                    value => TimeSpan.FromMinutes(value)));
                opts.SetRefreshTokenLifetime(ReadPositiveTimeSpan(
                    configuration,
                    "RefreshToken:TtlDays",
                    defaultValue: 7,
                    value => TimeSpan.FromDays(value)));

                ConfigureOpenIddictCertificates(opts, configuration, environment);

                opts.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();

                opts.AddEventHandler<ApplyTokenResponseContext>(b =>
                    b.UseSingletonHandler<ApplyRefreshTokenCookieHandler>());

                opts.AddEventHandler<ExtractTokenRequestContext>(b =>
                    b.UseSingletonHandler<ExtractRefreshTokenFromCookieHandler>());
            })
            .AddValidation(opts =>
            {
                opts.UseLocalServer();
                opts.UseAspNetCore();
                opts.AddEventHandler<OpenIddict.Validation.OpenIddictValidationEvents.ProcessAuthenticationContext>(b =>
                    b.UseScopedHandler<ValidateJtiBlacklistHandler>()
                        .AddFilter<OpenIddict.Validation.OpenIddictValidationHandlerFilters.RequireAccessTokenValidated>());
            });
    }

    private static void ConfigureOpenIddictCertificates(
        OpenIddictServerBuilder opts,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopmentOrTesting())
        {
            opts.AddEphemeralEncryptionKey()
                .AddEphemeralSigningKey();
            return;
        }

        string signingThumbprint = RequiredValue(configuration, "OpenIddict:Certificates:SigningThumbprint");
        string encryptionThumbprint = configuration["OpenIddict:Certificates:EncryptionThumbprint"]
            ?? signingThumbprint;
        StoreName storeName = ReadEnum(configuration, "OpenIddict:Certificates:StoreName", StoreName.My);
        StoreLocation storeLocation = ReadEnum(
            configuration,
            "OpenIddict:Certificates:StoreLocation",
            StoreLocation.LocalMachine);

        opts.AddSigningCertificate(signingThumbprint, storeName, storeLocation)
            .AddEncryptionCertificate(encryptionThumbprint, storeName, storeLocation);
    }

    private static void AddAxisAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    }

    private static void AddAxisForwardedHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(opts =>
        {
            opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });
    }

    private static void AddAxisRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        int defaultPermitLimit = environment.IsTesting() ? 1_000 : 10;
        int permitLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", defaultPermitLimit);
        TimeSpan window = TimeSpan.FromSeconds(
            configuration.GetValue("RateLimiting:Auth:WindowSeconds", 60));

        services.AddRateLimiter(opts =>
        {
            opts.AddPolicy(AuthRateLimiterPolicy, context =>
            {
                string partitionKey = context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown-client";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0,
                    });
            });

            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });
    }

    private static void AddAxisModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityInfrastructure(configuration);
        services.AddDataModelingInfrastructure(configuration);
        services.AddWorkflowBuilderInfrastructure(configuration);
        services.AddFormBuilderInfrastructure(configuration);
        services.AddWorkflowEngineInfrastructure(configuration);
    }

    private static void AddAxisGrpcClients(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        string? configuredUrl = configuration["Modules:Identity:GrpcUrl"];
        string identityGrpcUrl = !string.IsNullOrWhiteSpace(configuredUrl)
            ? configuredUrl
            : environment.IsDevelopmentOrTesting()
                ? "http://localhost:5280"
                : throw new InvalidOperationException("Modules:Identity:GrpcUrl is required");

        if (!Uri.TryCreate(identityGrpcUrl, UriKind.Absolute, out Uri? identityUri))
            throw new InvalidOperationException("Modules:Identity:GrpcUrl must be an absolute URI.");

        if (!environment.IsDevelopmentOrTesting() && identityUri.IsLoopback)
            throw new InvalidOperationException("Modules:Identity:GrpcUrl must not point to localhost outside Development/Testing.");

        services.AddGrpcClient<Axis.Identity.Contracts.Grpc.IdentityService.IdentityServiceClient>(opts =>
        {
            opts.Address = identityUri;
        });
    }

    private static void AddAxisRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(
                configuration["Redis:ConnectionString"]
                    ?? throw new InvalidOperationException("Redis:ConnectionString is required")));
        services.AddSingleton<IJtiBlacklist, RedisJtiBlacklist>();
    }

    private static void AddAxisRequestContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ITenantContext, HttpTenantContext>();
    }

    private static void AddAxisCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string[] allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        services.AddCors(opts => opts.AddPolicy("SpaOrigin", policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
    }

    private static void AddAxisJson(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(opts =>
        {
            opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.SerializerOptions.PropertyNameCaseInsensitive = true;
            opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            opts.SerializerOptions.Converters.Add(new AddFieldRequestConverter());
            opts.SerializerOptions.Converters.Add(new UpdateFieldRequestConverter());
            opts.SerializerOptions.Converters.Add(new AddDataClassFieldRequestConverter());
            opts.SerializerOptions.Converters.Add(new AddFormFieldRequestConverter());
        });

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    }

    private static void AddAxisOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(opts =>
        {
            opts.SwaggerDoc("v1", new OpenApiInfo { Title = "Axis API", Version = "v1" });
            opts.SupportNonNullableReferenceTypes();
            opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            });
            opts.OperationFilter<AuthorizeOperationFilter>();
        });
    }

    private static void AddAxisHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
    }

    private static TimeSpan ReadPositiveTimeSpan(
        IConfiguration configuration,
        string key,
        int defaultValue,
        Func<int, TimeSpan> factory)
    {
        int value = configuration.GetValue(key, defaultValue);
        if (value <= 0)
            throw new InvalidOperationException($"{key} must be greater than zero.");

        return factory(value);
    }

    private static TEnum ReadEnum<TEnum>(
        IConfiguration configuration,
        string key,
        TEnum defaultValue)
        where TEnum : struct
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
            return parsed;

        throw new InvalidOperationException($"{key} has invalid value '{value}'.");
    }

    private static string RequiredValue(IConfiguration configuration, string key) =>
        configuration[key]
        ?? throw new InvalidOperationException($"{key} is required");
}
