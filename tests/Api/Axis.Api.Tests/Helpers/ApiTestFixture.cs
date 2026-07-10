using System.Text.Json;
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
    private string? _previousBusinessObjectsConnectionStringEnv;
    private string? _previousRulesConnectionStringEnv;
    private WebApplicationFactory<Program> _factory = null!;
    private string _identityConnectionString = null!;
    private string _businessObjectsConnectionString = null!;
    private string _rulesConnectionString = null!;

    private readonly CapturingEmailSender _emailCapture = new();

    public HttpClient Client { get; private set; } = null!;
    public CapturingEmailSender EmailCapture => _emailCapture;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        string postgresAdminConnectionString = _postgres.GetConnectionString();
        _identityConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_identity_test");
        _businessObjectsConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_business_objects_test");
        _rulesConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_rules_test");

        _previousIdentityConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Identity");
        _previousBusinessObjectsConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__BusinessObjects");
        _previousRulesConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Rules");
        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _identityConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__BusinessObjects", _businessObjectsConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Rules", _rulesConnectionString);

        DbContextOptions<IdentityDbContext> identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_identityConnectionString)
            .UseOpenIddict()
            .Options;
        await using (IdentityDbContext identityCtx = new(identityOptions))
        {
            await identityCtx.Database.MigrateAsync();
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
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        if (_dataProtectionKeysDirectory.Exists)
            _dataProtectionKeysDirectory.Delete(recursive: true);

        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _previousIdentityConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__BusinessObjects", _previousBusinessObjectsConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__Rules", _previousRulesConnectionStringEnv);
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    private static async Task SeedTestOpenIddictClientAsync(IServiceProvider services)
    {
        IOpenIddictApplicationManager appManager =
            services.GetRequiredService<IOpenIddictApplicationManager>();

        if (await appManager.FindByClientIdAsync("axis_spa") is null)
        {
            await appManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "axis_spa",
                ClientType = ClientTypes.Public,
                DisplayName = "Axis Platform Web (Test)",
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
                    new Uri("https://localhost:3000/callback"),
                    new Uri("https://localhost/callback"),
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
