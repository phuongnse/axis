using System.Text.Json;
using Axis.DataModeling.Contracts.Grpc;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Contracts.Grpc;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Services;
using Axis.Shared.Application.Workspaces;
using Axis.Shared.Infrastructure.Workspaces;
using Axis.Testing;
using Axis.WorkflowBuilder.Contracts.Grpc;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
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

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:7.7.0")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    private string? _previousIdentityConnectionStringEnv;
    private string? _previousDataModelingConnectionStringEnv;
    private string? _previousWorkflowBuilderConnectionStringEnv;
    private string? _previousFormBuilderConnectionStringEnv;
    private string? _previousWorkflowEngineConnectionStringEnv;
    private string? _previousKafkaBrokersEnv;
    private string? _previousRabbitMqConnectionStringEnv;

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

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        // Remote Docker endpoints exposed from WSL2 are more reliable when
        // container lifecycle calls are serialized.
        await _postgres.StartAsync();

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

        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _identityConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__DataModeling", _dataModelingConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowBuilder", _workflowBuilderConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__FormBuilder", _formBuilderConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowEngine", _workflowEngineConnectionString);

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
            opts => new DataModelingDbContext(opts, new PublicSchemaWorkspaceContext()));
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowBuilderDbContext>(
            _workflowBuilderConnectionString,
            opts => new WorkflowBuilderDbContext(opts, new PublicSchemaWorkspaceContext()));
        await PostgresModuleTestDatabase.MigrateAsync<FormBuilderDbContext>(
            _formBuilderConnectionString,
            opts => new FormBuilderDbContext(opts, new PublicSchemaWorkspaceContext()));
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowEngineDbContext>(
            _workflowEngineConnectionString,
            opts => new WorkflowEngineDbContext(opts, new PublicSchemaWorkspaceContext()));

        await _redis.StartAsync();
        await _rabbitMq.StartAsync();
        await _kafka.StartAsync();

        Environment.SetEnvironmentVariable("Kafka__Brokers", _kafka.GetBootstrapAddress());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _rabbitMq.GetConnectionString());

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
                    ["Modules:DataModeling:GrpcUrl"] = "http://localhost",
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
                services.RemoveAll<DataModelCatalogService.DataModelCatalogServiceClient>();
                services.AddGrpcClient<DataModelCatalogService.DataModelCatalogServiceClient>(options =>
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
                services.RemoveAll<IWorkspaceLogoStorageService>();
                services.AddScoped<IWorkspaceLogoStorageService, NullWorkspaceLogoStorageService>();

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
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _kafka.DisposeAsync();
        await _rabbitMq.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _previousIdentityConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__DataModeling", _previousDataModelingConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowBuilder", _previousWorkflowBuilderConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__FormBuilder", _previousFormBuilderConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__WorkflowEngine", _previousWorkflowEngineConnectionStringEnv);
        Environment.SetEnvironmentVariable("Kafka__Brokers", _previousKafkaBrokersEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _previousRabbitMqConnectionStringEnv);
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public HttpClient CreateNewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });

    public async Task EnsureWorkspaceProvisionedAsync(string adminEmail)
    {
        Email email = Email.Create(adminEmail).Value!;

        Guid workspaceId;
        await using (IdentityDbContext identityContext = new(
                         new DbContextOptionsBuilder<IdentityDbContext>()
                             .UseNpgsql(_identityConnectionString)
                             .UseOpenIddict()
                             .Options))
        {
            User user = await identityContext.Users
                .SingleAsync(u => u.Email == email);
            workspaceId = await (
                from membership in identityContext.WorkspaceMemberships
                join workspace in identityContext.Workspaces on membership.workspaceId equals workspace.Id
                where membership.UserId == user.Id && workspace.Type == WorkspaceType.Team
                select workspace.Id)
                .SingleAsync();
        }

        await EnsureModuleSchemasAsync(workspaceId);
        await EnsureDataModelingTablesAsync(workspaceId);

        await using IdentityDbContext finalizeContext = new(
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(_identityConnectionString)
                .UseOpenIddict()
                .Options);
        Workspace Workspace = await finalizeContext.Workspaces
            .SingleAsync(o => o.Id == workspaceId);
        if (Workspace.Status == WorkspaceStatus.Provisioning)
        {
            Workspace.CompleteProvisioning();
            await finalizeContext.SaveChangesAsync();
        }
    }

    private async Task EnsureModuleSchemasAsync(Guid workspaceId)
    {
        string schema = $"workspace_{workspaceId:N}";
        await EnsureWorkspaceSchemaExistsAsync(_dataModelingConnectionString, schema);
        await PostgresModuleTestDatabase.MigrateAsync<DataModelingDbContext>(
            _dataModelingConnectionString,
            opts => new DataModelingDbContext(opts, new FixedWorkspaceContext(workspaceId)));

        await EnsureWorkspaceSchemaExistsAsync(_workflowBuilderConnectionString, schema);
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowBuilderDbContext>(
            _workflowBuilderConnectionString,
            opts => new WorkflowBuilderDbContext(opts, new FixedWorkspaceContext(workspaceId)));

        await EnsureWorkspaceSchemaExistsAsync(_formBuilderConnectionString, schema);
        await PostgresModuleTestDatabase.MigrateAsync<FormBuilderDbContext>(
            _formBuilderConnectionString,
            opts => new FormBuilderDbContext(opts, new FixedWorkspaceContext(workspaceId)));

        await EnsureWorkspaceSchemaExistsAsync(_workflowEngineConnectionString, schema);
        await PostgresModuleTestDatabase.MigrateAsync<WorkflowEngineDbContext>(
            _workflowEngineConnectionString,
            opts => new WorkflowEngineDbContext(opts, new FixedWorkspaceContext(workspaceId)));
    }

    private static async Task EnsureWorkspaceSchemaExistsAsync(string connectionString, string schema)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync();
    }

    private async Task EnsureDataModelingTablesAsync(Guid workspaceId)
    {
        string schema = $"workspace_{workspaceId:N}";
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await using NpgsqlConnection connection = new(_dataModelingConnectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = @schema
                      AND table_name = 'data_records')
                """;
            command.Parameters.AddWithValue("schema", schema);
            object? scalar = await command.ExecuteScalarAsync();
            if (scalar is bool exists && exists)
                return;

            await EnsureModuleSchemasAsync(workspaceId);
        }

        throw new InvalidOperationException(
            $"Workspace schema '{schema}' is missing data_records table after repeated migration attempts.");
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

internal sealed class PublicSchemaWorkspaceContext : IWorkspaceContext
{
    public Guid workspaceId => Guid.Empty;
    public string SchemaName => "public";
}

[CollectionDefinition("Api")]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>;
