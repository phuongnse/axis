using System.Text.Json;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Contracts.Grpc;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Services;
using Axis.Shared.Application.Tenancy;
using Axis.Testing;
using Axis.WorkflowBuilder.Contracts.Grpc;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;
using IDataModelingUnitOfWork = Axis.DataModeling.Application.Services.IUnitOfWork;
using IFormBuilderUnitOfWork = Axis.FormBuilder.Application.Services.IUnitOfWork;
using IWorkflowBuilderUnitOfWork = Axis.WorkflowBuilder.Application.Services.IUnitOfWork;

namespace Axis.Api.Tests.Helpers;

/// <summary>
/// Fixture for the end-to-end async-provisioning test suite.
///
/// Deliberately different from <see cref="ApiTestFixture"/> in one critical way:
/// <b>Identity's <c>IUnitOfWork</c> is kept as the real <c>IdentityUnitOfWork</c>.</b>
/// The real implementation collects domain events and publishes them via
/// Wolverine's <c>IMessageBus</c>. Without it, <c>verify-email</c> would never emit
/// <c>OrganizationVerifiedEvent</c> into the local message queue and the
/// multi-module tenant-provisioning pipeline would never start.
///
/// <see cref="ApiTestFixture"/> intentionally suppresses event publishing so that
/// endpoint tests can provision schemas deterministically without racing against the
/// async pipeline. The two fixtures serve different concerns and must remain separate.
/// </summary>
public sealed class ProvisioningE2EFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.7.0")
        // Eliminates the 3-second initial consumer-group rebalance delay (test-only setting).
        .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    // In-process Schema Registry so Wolverine's Avro serializer can register and
    // look up schemas over HTTP without a separate container. The modulith runs all
    // producers and consumers in the same process, so a single in-memory registry
    // is correct and sufficient — no distributed consistency issue.
    private InProcessSchemaRegistryServer _schemaRegistry = null!;

    private string? _previousIdentityConnectionStringEnv;
    private string? _previousDataModelingConnectionStringEnv;
    private string? _previousWorkflowBuilderConnectionStringEnv;
    private string? _previousFormBuilderConnectionStringEnv;
    private string? _previousWorkflowEngineConnectionStringEnv;
    private string? _previousKafkaBrokersEnv;
    private string? _previousRabbitMqConnectionStringEnv;
    private string? _previousSchemaRegistryUrlEnv;

    private WebApplicationFactory<Program> _factory = null!;
    private string _postgresAdminConnectionString = null!;
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
        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _kafka.StartAsync(),
            _rabbitMq.StartAsync());

        // Start the in-process Schema Registry before the WebApplicationFactory so
        // its URL is known when Wolverine reads SchemaRegistry:Url during host build.
        _schemaRegistry = await InProcessSchemaRegistryServer.StartAsync();

        _postgresAdminConnectionString = _postgres.GetConnectionString();
        _identityConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(_postgresAdminConnectionString, "axis_identity_test");
        _dataModelingConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(_postgresAdminConnectionString, "axis_datamodeling_test");
        _workflowBuilderConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(_postgresAdminConnectionString, "axis_workflowbuilder_test");
        _formBuilderConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(_postgresAdminConnectionString, "axis_formbuilder_test");
        _workflowEngineConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(_postgresAdminConnectionString, "axis_workflowengine_test");

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
        Environment.SetEnvironmentVariable("Kafka__Brokers", _kafka.GetBootstrapAddress());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _rabbitMq.GetConnectionString());
        // SchemaRegistry:Url must reach wolverineConfig in Program.cs via env var (belt-and-suspenders
        // alongside the in-memory config entry) because ConfigurationManager is populated from env vars
        // at WebApplicationBuilder construction time, before ConfigureAppConfiguration callbacks run.
        Environment.SetEnvironmentVariable("SchemaRegistry__Url", _schemaRegistry.BaseUrl);

        // Identity migrations (public schema only — tenant schemas are created by the provisioning pipeline)
        DbContextOptions<IdentityDbContext> identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_identityConnectionString)
            .UseOpenIddict()
            .Options;
        await using (IdentityDbContext identityCtx = new(identityOptions))
        {
            await identityCtx.Database.MigrateAsync();
            await SubscriptionPlanSeeder.EnsureWellKnownPlansAsync(identityCtx);
        }

        // Module base migrations (public schema only; tenant schemas are provisioned by the pipeline)
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
                    ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                    // Point the Avro serializer at the in-process Schema Registry.
                    // Kafka:UseEventTransport is intentionally absent — it defaults to true
                    // so this fixture exercises the real Kafka transport layer end-to-end.
                    ["SchemaRegistry:Url"] = _schemaRegistry.BaseUrl,
                    ["Modules:FormBuilder:GrpcUrl"] = "http://localhost",
                    ["Modules:WorkflowBuilder:GrpcUrl"] = "http://localhost",
                });
            });

            builder.ConfigureTestServices(services =>
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
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.AddDbContext<IdentityDbContext>(opts =>
                    opts.UseNpgsql(_identityConnectionString)
                        .UseOpenIddict());

                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(_redis.GetConnectionString()));

                services.RemoveAll<IEmailSender>();
                services.AddSingleton(_emailCapture);
                services.AddSingleton<IEmailSender>(_emailCapture);
                services.RemoveAll<IAvatarStorageService>();
                services.AddScoped<IAvatarStorageService, NullAvatarStorageService>();
                services.RemoveAll<IOrganizationLogoStorageService>();
                services.AddScoped<IOrganizationLogoStorageService, NullOrganizationLogoStorageService>();

                // NOTE: Identity IUnitOfWork is intentionally NOT replaced here.
                // The real IdentityUnitOfWork must run so that verify-email collects the
                // OrganizationVerified domain event and publishes OrganizationVerifiedEvent
                // via Wolverine's IMessageBus — which starts the async provisioning pipeline
                // that this test suite exercises. ApiTestFixture uses a no-op replacement
                // to keep endpoint tests deterministic; this fixture does not.

                // Module UoWs for DataModeling / WorkflowBuilder / FormBuilder are replaced
                // with no-ops: they suppress cross-module domain events from CRUD operations,
                // and the provisioning handlers (OrganizationVerifiedHandler,
                // TenantModuleProvisionAttempt) do not go through these UoWs at all.
                services.RemoveAll<IDataModelingUnitOfWork>();
                services.AddScoped<IDataModelingUnitOfWork>(sp =>
                    new NullDataModelingUnitOfWork(sp.GetRequiredService<DataModelingDbContext>()));

                services.RemoveAll<IWorkflowBuilderUnitOfWork>();
                services.AddScoped<IWorkflowBuilderUnitOfWork>(sp =>
                    new NullWorkflowBuilderUnitOfWork(sp.GetRequiredService<WorkflowBuilderDbContext>()));

                services.RemoveAll<IFormBuilderUnitOfWork>();
                services.AddScoped<IFormBuilderUnitOfWork>(sp =>
                    new NullFormBuilderUnitOfWork(sp.GetRequiredService<FormBuilderDbContext>()));

                services.PostConfigure<OpenIddictServerAspNetCoreOptions>(opts =>
                    opts.DisableTransportSecurityRequirement = true);

                ServiceDescriptor? openIddictSeederDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType == typeof(OpenIddictSeeder));
                if (openIddictSeederDescriptor is not null)
                    services.Remove(openIddictSeederDescriptor);
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

    // Polls /health/ready until the app is up. Wolverine's durable messaging
    // background services need at least one request cycle before the pipeline
    // can receive and route the OrganizationVerifiedEvent from the local queue.
    private async Task WaitForHealthyAsync()
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                HttpResponseMessage resp = await Client.GetAsync("/health/ready");
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch (Exception) { /* host not yet accepting connections */ }
            await Task.Delay(TimeSpan.FromMilliseconds(300));
        }
        throw new InvalidOperationException("Test host did not become healthy within 30 seconds.");
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _schemaRegistry.DisposeAsync();
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _kafka.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask());

        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _previousIdentityConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__DataModeling", _previousDataModelingConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowBuilder",
            _previousWorkflowBuilderConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__FormBuilder", _previousFormBuilderConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowEngine",
            _previousWorkflowEngineConnectionStringEnv);
        Environment.SetEnvironmentVariable("Kafka__Brokers", _previousKafkaBrokersEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _previousRabbitMqConnectionStringEnv);
        Environment.SetEnvironmentVariable("SchemaRegistry__Url", _previousSchemaRegistryUrlEnv);
    }

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

[CollectionDefinition("Api-E2E")]
public sealed class E2EApiTestCollection : ICollectionFixture<ProvisioningE2EFixture>;
