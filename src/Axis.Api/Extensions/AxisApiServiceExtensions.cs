using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Axis.Api.Authorization;
using Axis.Api.HealthChecks;
using Axis.Api.Infrastructure;
using Axis.Audit.Infrastructure.Extensions;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;
using Axis.BusinessObjects.Infrastructure.Extensions;
using Axis.Identity.Application.Commands.RegisterUser;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Services;
using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Rules.Infrastructure.Extensions;
using Axis.Shared.Application.Behaviors;
using Axis.Shared.Application.Identity;
using Axis.Shared.Infrastructure.Observability;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using Serilog;
using StackExchange.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Extensions;

internal static class AxisApiServiceExtensions
{
    private const string AuthRateLimiterPolicy = "auth";
    internal const string RulesRateLimiterPolicy = "rules";
    private const string AxisAuthenticationScheme = "Axis";
    internal const string BrowserSessionRotationScheme = "AxisBrowserSessionRotation";
    internal const string WorkspaceTransitionScheme = "AxisWorkspaceTransition";
    internal const string WorkspaceAccessPolicy = "WorkspaceAccess";

    public static WebApplicationBuilder AddAxisApiServices(this WebApplicationBuilder builder)
    {
        builder.AddAxisOpenTelemetry();
        builder.AddAxisLogging();

        builder.Services.AddAxisMediatR();
        IConnectionMultiplexer redis = builder.Services.AddAxisRedis(builder.Configuration);
        builder.Services.AddAxisDataProtection(builder.Configuration, builder.Environment, redis);
        builder.Services.AddAxisAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddAxisAuthorization();
        builder.Services.AddAxisForwardedHeaders();
        builder.Services.AddAxisRateLimiting(builder.Configuration, builder.Environment);
        builder.Services.AddAxisModules(builder.Configuration, builder.Environment);
        builder.Services.AddAxisRequestContext();
        builder.Services.AddAxisAntiforgery();
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
                typeof(RegisterUserCommand).Assembly,
                typeof(CreateBusinessObjectDefinitionCommand).Assembly,
                typeof(CreateBusinessObjectRecordCommand).Assembly,
                typeof(ListRuleDefinitionsQuery).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssemblies([
            typeof(RegisterUserCommand).Assembly,
            typeof(CreateBusinessObjectDefinitionCommand).Assembly,
            typeof(CreateBusinessObjectRecordCommand).Assembly,
            typeof(ListRuleDefinitionsQuery).Assembly,
        ]);
    }

    private static void AddAxisAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        AxisBrowserSessionPolicy sessionPolicy = AxisBrowserSessionPolicy.Load(configuration);
        TimeSpan refreshTokenLifetime = ReadPositiveTimeSpan(
            configuration,
            "Jwt:RefreshTokenTtlHours",
            defaultValue: 8,
            value => TimeSpan.FromHours(value));
        if (refreshTokenLifetime < sessionPolicy.AbsoluteLifetime)
        {
            throw new InvalidOperationException(
                "Jwt:RefreshTokenTtlHours cannot be shorter than BrowserSession:AbsoluteHours.");
        }

        services.AddSingleton(sessionPolicy);
        services.AddSingleton(new WorkspaceContextTransitionPolicy(
            TimeSpan.FromMinutes(5),
            sessionPolicy.AbsoluteLifetime + sessionPolicy.AbsoluteLifetime + TimeSpan.FromMinutes(5))
            .Validate());
        services.AddSingleton<RedisTicketStore>();
        services.AddSingleton<IWorkspaceTransitionTicketCleanup>(services =>
            services.GetRequiredService<RedisTicketStore>());
        services.AddSingleton<WorkspaceTransitionCleanupBatch>();
        services.AddScoped<AxisBrowserSessionIssuer>();
        services.AddScoped<WorkspaceContextTransitionSaga>();
        services.AddHostedService<WorkspaceTransitionCleanupService>();
        services.AddHostedService<WorkspaceTransitionExpiryService>();

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AxisAuthenticationScheme;
                options.DefaultChallengeScheme = AxisAuthenticationScheme;
                options.DefaultForbidScheme = AxisAuthenticationScheme;
            })
            .AddPolicyScheme(AxisAuthenticationScheme, AxisAuthenticationScheme, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey("Authorization")
                        ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, opts =>
            {
                ConfigureBrowserSessionCookie(opts, sessionPolicy);
                opts.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                opts.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
                opts.Events.OnValidatePrincipal = async context =>
                {
                    if (!AxisBrowserSessionPolicy.IsPastAbsoluteExpiry(
                            context.Properties,
                            DateTimeOffset.UtcNow))
                    {
                        return;
                    }

                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme);
                };
            })
            .AddCookie(BrowserSessionRotationScheme, opts =>
                ConfigureBrowserSessionCookie(opts, sessionPolicy))
            .AddCookie(WorkspaceTransitionScheme, ConfigureWorkspaceTransitionCookie);
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<RedisTicketStore, IDataProtectionProvider>(ConfigureBrowserSessionTicketStore);
        services.AddOptions<CookieAuthenticationOptions>(BrowserSessionRotationScheme)
            .Configure<RedisTicketStore, IDataProtectionProvider>(ConfigureBrowserSessionTicketStore);
        services.AddOptions<CookieAuthenticationOptions>(WorkspaceTransitionScheme)
            .Configure<RedisTicketStore, IDataProtectionProvider>(ConfigureBrowserSessionTicketStore);

        services.AddOpenIddict()
            .AddServer(opts =>
            {
                opts.SetIssuer(ReadRequiredAbsoluteHttpsUri(configuration, "OpenIddict:Issuer"))
                    .SetAuthorizationEndpointUris("/connect/authorize")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetPushedAuthorizationEndpointUris("/connect/par")
                    .SetRevocationEndpointUris("/connect/revoke")
                    .SetTokenEndpointUris("/connect/token");

                opts.RegisterScopes(Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess);

                opts.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow()
                    .RequireProofKeyForCodeExchange()
                    .EnableAuthorizationRequestCaching();

                opts.SetAccessTokenLifetime(ReadPositiveTimeSpan(
                    configuration,
                    "Jwt:AccessTokenTtlMinutes",
                    defaultValue: 15,
                    value => TimeSpan.FromMinutes(value)));
                opts.SetRefreshTokenLifetime(refreshTokenLifetime);
                ConfigureOpenIddictCertificates(opts, configuration, environment);

                opts.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(opts =>
            {
                opts.UseLocalServer();
                opts.UseAspNetCore();
            });

        services.Configure<OpenIddictServerOptions>(opts =>
            opts.RequestTokenLifetime = TimeSpan.FromMinutes(5));
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

    private static void ConfigureBrowserSessionCookie(
        CookieAuthenticationOptions options,
        AxisBrowserSessionPolicy sessionPolicy)
    {
        options.Cookie.Name = "__Host-axis-session";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = sessionPolicy.IdleLifetime;
        options.SlidingExpiration = true;
    }

    private static void ConfigureWorkspaceTransitionCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "__Host-axis-workspace-transition";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = false;
    }

    private static void ConfigureBrowserSessionTicketStore(
        CookieAuthenticationOptions options,
        RedisTicketStore store,
        IDataProtectionProvider dataProtectionProvider)
    {
        options.SessionStore = store;
        options.TicketDataFormat = new TicketDataFormat(
            dataProtectionProvider.CreateProtector("Axis.Api", "BrowserSessionCookie", "v1"));
    }

    private static void AddAxisAuthorization(this IServiceCollection services)
    {
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
            WorkspaceAccessAuthorizationHandler>();
        services.AddAuthorization(options => options.AddPolicy(
            WorkspaceAccessPolicy,
            policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new WorkspaceAccessRequirement())));
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
        int defaultRulesPermitLimit = environment.IsTesting() ? 1_000 : 120;
        int rulesPermitLimit = configuration.GetValue(
            "RateLimiting:Rules:PermitLimit",
            defaultRulesPermitLimit);
        TimeSpan rulesWindow = TimeSpan.FromSeconds(
            configuration.GetValue("RateLimiting:Rules:WindowSeconds", 60));

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

            opts.AddPolicy(RulesRateLimiterPolicy, context =>
            {
                string subject = context.User.FindFirst("sub")?.Value ?? "anonymous";
                string workspace = context.User.FindFirst("workspace_id")?.Value ?? "no-workspace";

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"{workspace}:{subject}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rulesPermitLimit,
                        Window = rulesWindow,
                        QueueLimit = 0,
                    });
            });

            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            opts.OnRejected = async (context, cancellationToken) =>
            {
                const int statusCode = StatusCodes.Status429TooManyRequests;
                ProblemDetails problem = ProblemDetailsDefaults.CreateProblemDetails(
                    statusCode,
                    "Too many requests. Please try again later.",
                    ProblemDetailsDefaults.RateLimitedCode,
                    "Too Many Requests");

                context.HttpContext.Response.StatusCode = statusCode;
                context.HttpContext.Response.ContentType = ProblemDetailsDefaults.JsonContentType;
                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });
    }

    private static void AddAxisModules(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddAuditInfrastructure(configuration);
        services.AddIdentityInfrastructure(configuration);
        services.AddRulesInfrastructure(configuration);
        services.AddBusinessObjectsInfrastructure(configuration);
    }

    private static IConnectionMultiplexer AddAxisRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException("Redis:ConnectionString is required");

        IConnectionMultiplexer redis = ConnectionMultiplexer.Connect(connectionString);
        services.AddSingleton(redis);
        return redis;
    }

    private static void AddAxisDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        IConnectionMultiplexer redis)
    {
        IDataProtectionBuilder dataProtection = services.AddDataProtection()
            .SetApplicationName("Axis.Api.BrowserSession")
            .PersistKeysToStackExchangeRedis(redis, "axis:data-protection:browser-session");

        if (!environment.IsDevelopmentOrTesting())
        {
            dataProtection.ProtectKeysWithCertificate(LoadCertificate(
                configuration,
                "DataProtection:CertificateThumbprint"));
        }
    }

    private static void AddAxisAntiforgery(this IServiceCollection services)
    {
        services.AddSingleton<Microsoft.AspNetCore.Antiforgery.IAntiforgeryAdditionalDataProvider,
            BrowserSessionAntiforgeryAdditionalDataProvider>();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-axis-antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.HeaderName = "X-CSRF-TOKEN";
        });
    }

    private static void AddAxisRequestContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
    }

    private static void AddAxisJson(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(opts =>
        {
            opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            opts.SerializerOptions.PropertyNameCaseInsensitive = true;
            opts.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
            opts.SwaggerDoc("v1", new OpenApiInfo { Title = "Axis Platform API", Version = "v1" });
            opts.SupportNonNullableReferenceTypes();
            opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            });
            opts.OperationFilter<AuthorizeOperationFilter>();
            opts.OperationFilter<RequiredIdempotencyKeyOperationFilter>();
            opts.SchemaFilter<ProblemDetailsSchemaFilter>();
        });
    }

    private static void AddAxisHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
            .AddCheck<IdentityAuditHealthCheck>("identity-audit");
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

    private static Uri ReadRequiredAbsoluteHttpsUri(IConfiguration configuration, string key)
    {
        string value = RequiredValue(configuration, key);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{key} must be an absolute HTTPS URL without credentials, query, or fragment.");
        }

        return uri;
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string key)
    {
        string thumbprint = RequiredValue(configuration, key);
        StoreName storeName = ReadEnum(configuration, "OpenIddict:Certificates:StoreName", StoreName.My);
        StoreLocation storeLocation = ReadEnum(
            configuration,
            "OpenIddict:Certificates:StoreLocation",
            StoreLocation.LocalMachine);
        using X509Store store = new(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        X509Certificate2Collection matches = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            validOnly: true);
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"{key} must identify exactly one valid certificate.");
    }
}
