using System.Text.Json;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Services;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using IDataModelingUnitOfWork = Axis.DataModeling.Application.Services.IUnitOfWork;
using IFormBuilderUnitOfWork = Axis.FormBuilder.Application.Services.IUnitOfWork;
using IWorkflowBuilderUnitOfWork = Axis.WorkflowBuilder.Application.Services.IUnitOfWork;
using Axis.Shared.Application.Tenancy;
using OpenIddict.Server.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
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

    private WebApplicationFactory<Program> _factory = null!;
    private string _dmConnectionString = null!;
    private string _wbConnectionString = null!;
    private string _fbConnectionString = null!;
    private string _weConnectionString = null!;

    public HttpClient Client { get; private set; } = null!;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Each module needs its own database so EnsureCreatedAsync creates tables correctly.
        _dmConnectionString = await CreateModuleDatabaseAsync("axis_dm_test");
        _wbConnectionString = await CreateModuleDatabaseAsync("axis_wb_test");
        _fbConnectionString = await CreateModuleDatabaseAsync("axis_fb_test");
        _weConnectionString = await CreateModuleDatabaseAsync("axis_we_test");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:DataModeling"] = _dmConnectionString,
                    ["ConnectionStrings:WorkflowBuilder"] = _wbConnectionString,
                    ["ConnectionStrings:FormBuilder"] = _fbConnectionString,
                    ["ConnectionStrings:WorkflowEngine"] = _weConnectionString,
                    ["ConnectionStrings:Wolverine"] = _postgres.GetConnectionString(),
                    ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Replace IdentityDbContext with test container connection
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.AddDbContext<IdentityDbContext>(opts =>
                    opts.UseNpgsql(_postgres.GetConnectionString())
                        .UseOpenIddict());

                // Replace Redis
                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(_redis.GetConnectionString()));

                // Replace external services with no-ops
                services.RemoveAll<IEmailSender>();
                services.AddScoped<IEmailSender, NullEmailSender>();
                services.RemoveAll<IAvatarStorageService>();
                services.AddScoped<IAvatarStorageService, NullAvatarStorageService>();

                // Replace IUnitOfWork — IdentityUnitOfWork requires Wolverine.IMessageBus
                // which is not registered in integration tests; domain events are irrelevant
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(sp =>
                    new NullUnitOfWork(sp.GetRequiredService<IdentityDbContext>()));

                services.RemoveAll<IDataModelingUnitOfWork>();
                services.AddScoped<IDataModelingUnitOfWork>(sp =>
                    new NullDataModelingUnitOfWork(sp.GetRequiredService<DataModelingDbContext>()));

                services.RemoveAll<IWorkflowBuilderUnitOfWork>();
                services.AddScoped<IWorkflowBuilderUnitOfWork>(sp =>
                    new NullWorkflowBuilderUnitOfWork(sp.GetRequiredService<WorkflowBuilderDbContext>()));

                services.RemoveAll<IFormBuilderUnitOfWork>();
                services.AddScoped<IFormBuilderUnitOfWork>(sp =>
                    new NullFormBuilderUnitOfWork(sp.GetRequiredService<FormBuilderDbContext>()));

                // Use a fixed "public" schema for all tenants
                services.RemoveAll<ITenantContext>();
                services.AddScoped<ITenantContext>(_ => new PublicSchemaTenantContext());

                services.RemoveAll<ITenantSchemaProvisioner>();
                services.AddScoped<ITenantSchemaProvisioner, NoOpTenantSchemaProvisioner>();

                // WebApplicationFactory uses HTTP, not HTTPS. Disable OpenIddict's transport
                // security check so the authorization endpoint is reachable in tests.
                services.PostConfigure<OpenIddictServerAspNetCoreOptions>(opts =>
                    opts.DisableTransportSecurityRequirement = true);

                // Remove OpenIddictSeeder: it is a hosted service that runs on app startup,
                // before EnsureCreatedAsync is called here, causing "relation does not exist".
                // The fixture seeds OpenIddict clients manually in SeedTestOpenIddictClientsAsync.
                ServiceDescriptor? seederDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(OpenIddictSeeder));
                if (seederDescriptor is not null)
                    services.Remove(seederDescriptor);
            });
        });

        using IServiceScope scope = _factory.Services.CreateScope();

        IdentityDbContext ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        // Seed the SPA client used by integration tests
        await SeedTestOpenIddictClientsAsync(scope.ServiceProvider);

        DbContextOptions<DataModelingDbContext> dmOptions = new DbContextOptionsBuilder<DataModelingDbContext>()
            .UseNpgsql(_dmConnectionString)
            .Options;
        await using DataModelingDbContext dmCtx = new(dmOptions, new PublicSchemaTenantContext());
        await dmCtx.Database.EnsureCreatedAsync();

        DbContextOptions<WorkflowBuilderDbContext> wbOptions = new DbContextOptionsBuilder<WorkflowBuilderDbContext>()
            .UseNpgsql(_wbConnectionString)
            .Options;
        await using WorkflowBuilderDbContext wbCtx = new(wbOptions, new PublicSchemaTenantContext());
        await wbCtx.Database.EnsureCreatedAsync();

        DbContextOptions<FormBuilderDbContext> fbOptions = new DbContextOptionsBuilder<FormBuilderDbContext>()
            .UseNpgsql(_fbConnectionString)
            .Options;
        await using FormBuilderDbContext fbCtx = new(fbOptions, new PublicSchemaTenantContext());
        await fbCtx.Database.EnsureCreatedAsync();

        DbContextOptions<WorkflowEngineDbContext> weOptions = new DbContextOptionsBuilder<WorkflowEngineDbContext>()
            .UseNpgsql(_weConnectionString)
            .Options;
        await using WorkflowEngineDbContext weCtx = new(weOptions, new PublicSchemaTenantContext());
        await weCtx.Database.EnsureCreatedAsync();

        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public HttpClient CreateNewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    private static async Task SeedTestOpenIddictClientsAsync(IServiceProvider services)
    {
        IOpenIddictApplicationManager appManager =
            services.GetRequiredService<IOpenIddictApplicationManager>();

        IOpenIddictScopeManager scopeManager =
            services.GetRequiredService<IOpenIddictScopeManager>();

        if (await scopeManager.FindByNameAsync("permissions") is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "permissions",
                Resources = { "axis_api" },
            });
        }

        if (await appManager.FindByClientIdAsync("axis_spa") is null)
        {
            await appManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "axis_spa",
                ClientType = ClientTypes.Public,
                DisplayName = "Axis SPA (Test)",
                Permissions =
                {
                    Permissions.Endpoints.Authorization,
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.AuthorizationCode,
                    Permissions.GrantTypes.RefreshToken,
                    Permissions.ResponseTypes.Code,
                    Permissions.Prefixes.Scope + Scopes.OpenId,
                    Permissions.Prefixes.Scope + Scopes.Email,
                    Permissions.Prefixes.Scope + Scopes.Profile,
                    Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                    Permissions.Prefixes.Scope + "permissions",
                },
                RedirectUris =
                {
                    new Uri("http://localhost:3000/callback"),
                    // Test redirect — we instruct the HTTP client not to follow redirects
                    new Uri("http://localhost/callback"),
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange,
                },
            });
        }
    }

    private async Task<string> CreateModuleDatabaseAsync(string dbName)
    {
        await using NpgsqlConnection conn = new(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        NpgsqlConnectionStringBuilder csb = new(_postgres.GetConnectionString())
        { Database = dbName };
        return csb.ToString();
    }
}

/// <summary>
/// Stub tenant context — all tests share the "public" schema.
/// </summary>
internal sealed class PublicSchemaTenantContext : ITenantContext
{
    public Guid OrganizationId => Guid.Empty;
    public string SchemaName => "public";
}

[CollectionDefinition("Api")]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>;
