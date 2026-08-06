using System.Net.Http.Json;
using System.Text.Json;
using Axis.Audit.Infrastructure.Persistence;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Services;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Axis.Api.Tests.Helpers;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly DirectoryInfo _dataProtectionKeysDirectory = new(
        Path.Combine(Path.GetTempPath(), "axis-api-tests", Guid.NewGuid().ToString("N"), "data-protection-keys"));

    private string? _previousIdentityConnectionStringEnv;
    private string? _previousAuditConnectionStringEnv;
    private string? _previousBusinessObjectsConnectionStringEnv;
    private string? _previousRulesConnectionStringEnv;
    private string? _previousRedisConnectionStringEnv;
    private WebApplicationFactory<Program> _factory = null!;
    private string _identityConnectionString = null!;
    private string _auditConnectionString = null!;
    private string _businessObjectsConnectionString = null!;
    private string _rulesConnectionString = null!;

    private readonly CapturingEmailSender _emailCapture = new();

    public HttpClient Client { get; private set; } = null!;
    public string CsrfToken { get; private set; } = null!;
    public CapturingEmailSender EmailCapture => _emailCapture;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        string postgresAdminConnectionString = _postgres.GetConnectionString();
        _identityConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_identity_test");
        _auditConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_audit_test");
        _businessObjectsConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_business_objects_test");
        _rulesConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_rules_test");

        _previousIdentityConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Identity");
        _previousAuditConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Audit");
        _previousBusinessObjectsConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__BusinessObjects");
        _previousRulesConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Rules");
        _previousRedisConnectionStringEnv = Environment.GetEnvironmentVariable("Redis__ConnectionString");
        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _identityConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Audit", _auditConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__BusinessObjects", _businessObjectsConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Rules", _rulesConnectionString);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", _redis.GetConnectionString());

        DbContextOptions<IdentityDbContext> identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_identityConnectionString)
            .UseOpenIddict()
            .Options;
        await using (IdentityDbContext identityCtx = new(identityOptions))
        {
            await identityCtx.Database.MigrateAsync();
        }
        DbContextOptions<AuditDbContext> auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(_auditConnectionString)
            .Options;
        await using (AuditDbContext auditCtx = new(auditOptions))
        {
            await auditCtx.Database.MigrateAsync();
        }
        DbContextOptions<BusinessObjectsDbContext> objectsOptions = new DbContextOptionsBuilder<BusinessObjectsDbContext>()
            .UseNpgsql(_businessObjectsConnectionString)
            .Options;
        await using (BusinessObjectsDbContext objectsCtx = new(objectsOptions))
        {
            await objectsCtx.Database.MigrateAsync();
        }
        DbContextOptions<RulesDbContext> rulesOptions = new DbContextOptionsBuilder<RulesDbContext>()
            .UseNpgsql(_rulesConnectionString)
            .Options;
        await using (RulesDbContext rulesCtx = new(rulesOptions))
        {
            await rulesCtx.Database.MigrateAsync();
        }

        _dataProtectionKeysDirectory.Create();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = _identityConnectionString,
                    ["ConnectionStrings:Audit"] = _auditConnectionString,
                    ["ConnectionStrings:BusinessObjects"] = _businessObjectsConnectionString,
                    ["ConnectionStrings:Rules"] = _rulesConnectionString,
                    ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection()
                    .PersistKeysToFileSystem(_dataProtectionKeysDirectory)
                    .SetApplicationName("Axis.Api.Tests");

                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.AddDbContext<IdentityDbContext>(opts =>
                    opts.UseNpgsql(_identityConnectionString)
                        .UseOpenIddict());

                services.RemoveAll<DbContextOptions<AuditDbContext>>();
                services.RemoveAll<AuditDbContext>();
                services.AddDbContext<AuditDbContext>(opts =>
                    opts.UseNpgsql(_auditConnectionString));

                services.RemoveAll<DbContextOptions<BusinessObjectsDbContext>>();
                services.RemoveAll<BusinessObjectsDbContext>();
                services.AddDbContext<BusinessObjectsDbContext>(opts =>
                    opts.UseNpgsql(_businessObjectsConnectionString));

                services.RemoveAll<DbContextOptions<RulesDbContext>>();
                services.RemoveAll<RulesDbContext>();
                services.AddDbContext<RulesDbContext>(opts =>
                    opts.UseNpgsql(_rulesConnectionString));

                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(_redis.GetConnectionString()));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(_emailCapture);
                services.AddSingleton<IEmailSender>(_emailCapture);

                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(sp =>
                    new NullUnitOfWork(sp.GetRequiredService<IdentityDbContext>()));

                ServiceDescriptor? openIddictSeederDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(OpenIddictSeeder));
                if (openIddictSeederDescriptor is not null)
                    services.Remove(openIddictSeederDescriptor);
            });
        });

        using IServiceScope scope = _factory.Services.CreateScope();
        await SeedTestOpenIddictClientAsync(scope.ServiceProvider);

        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        await RefreshBrowserSecurityContextAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        if (_dataProtectionKeysDirectory.Exists)
            _dataProtectionKeysDirectory.Delete(recursive: true);

        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _previousIdentityConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__Audit", _previousAuditConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__BusinessObjects", _previousBusinessObjectsConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__Rules", _previousRulesConnectionStringEnv);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", _previousRedisConnectionStringEnv);
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public HttpClient CreateAnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    public async Task<JsonElement> RefreshBrowserSecurityContextAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement browserSession = await Client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session",
            JsonOptions,
            cancellationToken);
        CsrfToken = browserSession.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The browser session did not return an antiforgery token.");
        Client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", CsrfToken);
        return browserSession;
    }

    public async Task<HttpResponseMessage> SendBrowserMutationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        await RefreshBrowserSecurityContextAsync(cancellationToken);
        return await Client.SendAsync(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> PostBrowserJsonAsync<TValue>(
        string requestUri,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        await RefreshBrowserSecurityContextAsync(cancellationToken);
        return await Client.PostAsJsonAsync(requestUri, value, JsonOptions, cancellationToken);
    }

    public async Task<HttpResponseMessage> PostBrowserAsync(
        string requestUri,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        await RefreshBrowserSecurityContextAsync(cancellationToken);
        return await Client.PostAsync(requestUri, content, cancellationToken);
    }

    private static async Task SeedTestOpenIddictClientAsync(IServiceProvider services)
    {
        IOpenIddictApplicationManager appManager =
            services.GetRequiredService<IOpenIddictApplicationManager>();

        if (await appManager.FindByClientIdAsync("axis_mcp") is null)
        {
            await appManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "axis_mcp",
                ClientType = ClientTypes.Public,
                DisplayName = "Axis MCP local client (Test)",
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + Scopes.Email,
                    Permissions.Prefixes.Scope + Scopes.Profile,
                },
                RedirectUris =
                {
                    new Uri("http://127.0.0.1:48123/callback"),
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange,
                },
            });
        }
    }
}

[CollectionDefinition("Api")]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>;
