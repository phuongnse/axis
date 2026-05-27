using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Contracts.Grpc;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Services;
using Axis.Testing;
using Axis.Testing.Messaging;
using Axis.Testing.TestDoubles;
using Axis.WorkflowBuilder.Contracts.Grpc;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using StackExchange.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;
using IDataModelingUnitOfWork = Axis.DataModeling.Application.Services.IUnitOfWork;
using IFormBuilderUnitOfWork = Axis.FormBuilder.Application.Services.IUnitOfWork;
using IWorkflowBuilderUnitOfWork = Axis.WorkflowBuilder.Application.Services.IUnitOfWork;

namespace Axis.Messaging.Integration.Tests.Fixtures;

/// <summary>
/// Full-stack API host for Kafka + Schema Registry integration tests.
/// Keeps the real <see cref="IUnitOfWork"/> (Identity) so domain events publish to Kafka;
/// module UoWs stay no-op to avoid unrelated cross-module events during provisioning E2E.
/// </summary>
public sealed class MessagingApiHostFixture : IAsyncLifetime
{
    private readonly MessagingTestInfrastructure _messaging = new();

    private string? _previousIdentityConnectionStringEnv;
    private string? _previousDataModelingConnectionStringEnv;
    private string? _previousWorkflowBuilderConnectionStringEnv;
    private string? _previousFormBuilderConnectionStringEnv;
    private string? _previousWorkflowEngineConnectionStringEnv;
    private string? _previousKafkaBrokersEnv;
    private string? _previousRabbitMqConnectionStringEnv;
    private string? _previousSchemaRegistryUrlEnv;

    private WebApplicationFactory<Program> _factory = null!;
    private string _identityConnectionString = null!;
    private string _dataModelingConnectionString = null!;
    private string _workflowBuilderConnectionString = null!;
    private string _formBuilderConnectionString = null!;
    private string _workflowEngineConnectionString = null!;

    public HttpClient Client { get; private set; } = null!;

    private readonly CapturingEmailSender _emailCapture = new();
    public CapturingEmailSender EmailCapture => _emailCapture;

    public static readonly JsonSerializerOptions JsonOptions = ApiTestFixture.JsonOptions;

    public async Task InitializeAsync()
    {
        await _messaging.StartAsync();

        string postgresAdmin = _messaging.PostgresAdminConnectionString;
        _identityConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdmin, "axis_identity_messaging_test");
        _dataModelingConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdmin, "axis_datamodeling_messaging_test");
        _workflowBuilderConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdmin, "axis_workflowbuilder_messaging_test");
        _formBuilderConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdmin, "axis_formbuilder_messaging_test");
        _workflowEngineConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdmin, "axis_workflowengine_messaging_test");

        SaveAndSetEnvironmentVariables();

        DbContextOptions<IdentityDbContext> identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_identityConnectionString)
            .UseOpenIddict()
            .Options;
        await using (IdentityDbContext identityCtx = new(identityOptions))
        {
            await identityCtx.Database.MigrateAsync();
            await SubscriptionPlanSeeder.EnsureWellKnownPlansAsync(identityCtx);
        }

        await PostgresModuleTestDatabase.MigrateAsync<DataModelingDbContext>(
            _dataModelingConnectionString,
            opts => new DataModelingDbContext(opts, new PublicSchemaTenantContext()));
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowBuilderDbContext>(
            _workflowBuilderConnectionString,
            opts => new WorkflowBuilderDbContext(opts, new PublicSchemaTenantContext()));
        await PostgresModuleTestDatabase.MigrateAsync<FormBuilderDbContext>(
            _formBuilderConnectionString,
            opts => new FormBuilderDbContext(opts, new PublicSchemaTenantContext()));
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowEngineDbContext>(
            _workflowEngineConnectionString,
            opts => new WorkflowEngineDbContext(opts, new PublicSchemaTenantContext()));

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        WebApplicationFactory<Program> factory = null!;
        Lazy<HttpMessageHandler> grpcTestServerHandler =
            new(() => factory.Server.CreateHandler());

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = _identityConnectionString,
                    ["ConnectionStrings:DataModeling"] = _dataModelingConnectionString,
                    ["ConnectionStrings:WorkflowBuilder"] = _workflowBuilderConnectionString,
                    ["ConnectionStrings:FormBuilder"] = _formBuilderConnectionString,
                    ["ConnectionStrings:WorkflowEngine"] = _workflowEngineConnectionString,
                    ["Redis:ConnectionString"] = _messaging.RedisConnectionString,
                    ["Kafka:Brokers"] = _messaging.KafkaBootstrapAddress,
                    ["ConnectionStrings:RabbitMq"] = _messaging.RabbitMqConnectionString,
                    ["SchemaRegistry:Url"] = _messaging.SchemaRegistryUrl,
                    ["Modules:FormBuilder:GrpcUrl"] = "http://localhost",
                    ["Modules:WorkflowBuilder:GrpcUrl"] = "http://localhost",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                ConfigureGrpcTestClients(services, grpcTestServerHandler);
                ConfigureIdentityDb(services);
                ConfigureRedis(services);
                ConfigureTestDoubles(services);
                ConfigureModuleNoOpUnitOfWorks(services);
                DisableOpenIddictTransportSecurity(services);
                RemoveOpenIddictSeeder(services);
            });
        });

        _factory = factory;

        using IServiceScope scope = _factory.Services.CreateScope();
        await SeedTestOpenIddictClientsAsync(scope.ServiceProvider);

        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        await WaitForHealthyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _messaging.DisposeAsync();
        RestoreEnvironmentVariables();
    }

    public HttpClient CreateNewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    private void SaveAndSetEnvironmentVariables()
    {
        _previousIdentityConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Identity");
        _previousDataModelingConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__DataModeling");
        _previousWorkflowBuilderConnectionStringEnv =
            Environment.GetEnvironmentVariable("ConnectionStrings__WorkflowBuilder");
        _previousFormBuilderConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__FormBuilder");
        _previousWorkflowEngineConnectionStringEnv =
            Environment.GetEnvironmentVariable("ConnectionStrings__WorkflowEngine");
        _previousKafkaBrokersEnv = Environment.GetEnvironmentVariable("Kafka__Brokers");
        _previousRabbitMqConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__RabbitMq");
        _previousSchemaRegistryUrlEnv = Environment.GetEnvironmentVariable("SchemaRegistry__Url");

        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _identityConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__DataModeling", _dataModelingConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowBuilder", _workflowBuilderConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__FormBuilder", _formBuilderConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowEngine", _workflowEngineConnectionString);
        Environment.SetEnvironmentVariable("Kafka__Brokers", _messaging.KafkaBootstrapAddress);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _messaging.RabbitMqConnectionString);
        Environment.SetEnvironmentVariable("SchemaRegistry__Url", _messaging.SchemaRegistryUrl);
    }

    private void RestoreEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _previousIdentityConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__DataModeling", _previousDataModelingConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowBuilder", _previousWorkflowBuilderConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__FormBuilder", _previousFormBuilderConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowEngine", _previousWorkflowEngineConnectionStringEnv);
        Environment.SetEnvironmentVariable("Kafka__Brokers", _previousKafkaBrokersEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _previousRabbitMqConnectionStringEnv);
        Environment.SetEnvironmentVariable("SchemaRegistry__Url", _previousSchemaRegistryUrlEnv);
    }

    private async Task WaitForHealthyAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                HttpResponseMessage resp = await Client.GetAsync("/health/ready");
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch (Exception)
            {
                // host not yet accepting connections
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }

        throw new InvalidOperationException("Messaging test host did not become healthy within 60 seconds.");
    }

    private void ConfigureGrpcTestClients(IServiceCollection services, Lazy<HttpMessageHandler> grpcTestServerHandler)
    {
        services.RemoveAll<FormModelReferenceService.FormModelReferenceServiceClient>();
        services.AddGrpcClient<FormModelReferenceService.FormModelReferenceServiceClient>(options =>
        {
            options.Address = new Uri("http://localhost");
        })
        .ConfigurePrimaryHttpMessageHandler(() => grpcTestServerHandler.Value);
        services.RemoveAll<WorkflowFormReferenceService.WorkflowFormReferenceServiceClient>();
        services.AddGrpcClient<WorkflowFormReferenceService.WorkflowFormReferenceServiceClient>(options =>
        {
            options.Address = new Uri("http://localhost");
        })
        .ConfigurePrimaryHttpMessageHandler(() => grpcTestServerHandler.Value);
    }

    private void ConfigureIdentityDb(IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<IdentityDbContext>>();
        services.RemoveAll<IdentityDbContext>();
        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseNpgsql(_identityConnectionString)
                .UseOpenIddict());
    }

    private void ConfigureRedis(IServiceCollection services)
    {
        services.RemoveAll<IConnectionMultiplexer>();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(_messaging.RedisConnectionString));
    }

    private void ConfigureTestDoubles(IServiceCollection services)
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton(_emailCapture);
        services.AddSingleton<IEmailSender>(_emailCapture);
        services.RemoveAll<IAvatarStorageService>();
        services.AddScoped<IAvatarStorageService, NullAvatarStorageService>();
        services.RemoveAll<IOrganizationLogoStorageService>();
        services.AddScoped<IOrganizationLogoStorageService, NullOrganizationLogoStorageService>();
    }

    private static void ConfigureModuleNoOpUnitOfWorks(IServiceCollection services)
    {
        services.RemoveAll<IDataModelingUnitOfWork>();
        services.AddScoped<IDataModelingUnitOfWork>(sp =>
            new NullDataModelingUnitOfWork(sp.GetRequiredService<DataModelingDbContext>()));

        services.RemoveAll<IWorkflowBuilderUnitOfWork>();
        services.AddScoped<IWorkflowBuilderUnitOfWork>(sp =>
            new NullWorkflowBuilderUnitOfWork(sp.GetRequiredService<WorkflowBuilderDbContext>()));

        services.RemoveAll<IFormBuilderUnitOfWork>();
        services.AddScoped<IFormBuilderUnitOfWork>(sp =>
            new NullFormBuilderUnitOfWork(sp.GetRequiredService<FormBuilderDbContext>()));
    }

    private static void DisableOpenIddictTransportSecurity(IServiceCollection services) =>
        services.PostConfigure<OpenIddictServerAspNetCoreOptions>(opts =>
            opts.DisableTransportSecurityRequirement = true);

    private static void RemoveOpenIddictSeeder(IServiceCollection services)
    {
        ServiceDescriptor? openIddictSeederDescriptor = services.FirstOrDefault(
            d => d.ImplementationType == typeof(OpenIddictSeeder));
        if (openIddictSeederDescriptor is not null)
            services.Remove(openIddictSeederDescriptor);
    }

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
                    new Uri("http://localhost/callback"),
                },
                Requirements =
                {
                    Requirements.Features.ProofKeyForCodeExchange,
                },
            });
        }
    }
}

[CollectionDefinition("Messaging")]
public sealed class MessagingIntegrationTestCollection : ICollectionFixture<MessagingApiHostFixture>;
