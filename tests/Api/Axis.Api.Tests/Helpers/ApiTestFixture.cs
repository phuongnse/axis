using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Axis.Api.Infrastructure;
using Axis.Audit.Infrastructure.Persistence;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Persistence;
using Axis.Solutions.Infrastructure.Services;
using Axis.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using static OpenIddict.Abstractions.OpenIddictConstants;
using IdentitySubjectKind = Axis.Identity.Contracts.SubjectKind;

namespace Axis.Api.Tests.Helpers;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly ConcurrentDictionary<(Guid WorkspaceId, Guid SubjectId), Func<ProductAuthorizationRequest, ProductAuthorizationDecision>>
        _productAuthorizationTestAccess = new();
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
    private string? _previousAuthorizationConnectionStringEnv;
    private string? _previousBusinessObjectsConnectionStringEnv;
    private string? _previousRulesConnectionStringEnv;
    private string? _previousSolutionsConnectionStringEnv;
    private string? _previousRedisConnectionStringEnv;
    private WebApplicationFactory<Program> _factory = null!;
    private string _identityConnectionString = null!;
    private string _auditConnectionString = null!;
    private string _authorizationConnectionString = null!;
    private string _businessObjectsConnectionString = null!;
    private string _rulesConnectionString = null!;
    private string _solutionsConnectionString = null!;

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
        _authorizationConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_authorization_test");
        _businessObjectsConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_business_objects_test");
        _rulesConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_rules_test");
        _solutionsConnectionString =
            await PostgresModuleTestDatabase.CreateAsync(postgresAdminConnectionString, "axis_solutions_test");

        _previousIdentityConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Identity");
        _previousAuditConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Audit");
        _previousAuthorizationConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Authorization");
        _previousBusinessObjectsConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__BusinessObjects");
        _previousRulesConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Rules");
        _previousSolutionsConnectionStringEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Solutions");
        _previousRedisConnectionStringEnv = Environment.GetEnvironmentVariable("Redis__ConnectionString");
        Environment.SetEnvironmentVariable("ConnectionStrings__Identity", _identityConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Audit", _auditConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Authorization", _authorizationConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__BusinessObjects", _businessObjectsConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Rules", _rulesConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__Solutions", _solutionsConnectionString);
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
        DbContextOptions<AuthorizationDbContext> authorizationOptions = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(_authorizationConnectionString)
            .Options;
        await using (AuthorizationDbContext authorizationCtx = new(authorizationOptions))
        {
            await authorizationCtx.Database.MigrateAsync();
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
        DbContextOptions<SolutionsDbContext> solutionsOptions = new DbContextOptionsBuilder<SolutionsDbContext>()
            .UseNpgsql(_solutionsConnectionString)
            .Options;
        await using (SolutionsDbContext solutionsCtx = new(solutionsOptions))
        {
            await solutionsCtx.Database.MigrateAsync();
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
                    ["ConnectionStrings:Authorization"] = _authorizationConnectionString,
                    ["ConnectionStrings:BusinessObjects"] = _businessObjectsConnectionString,
                    ["ConnectionStrings:Rules"] = _rulesConnectionString,
                    ["ConnectionStrings:Solutions"] = _solutionsConnectionString,
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

                services.RemoveAll<DbContextOptions<AuthorizationDbContext>>();
                services.RemoveAll<AuthorizationDbContext>();
                services.AddDbContext<AuthorizationDbContext>(opts =>
                    opts.UseNpgsql(_authorizationConnectionString));

                services.RemoveAll<DbContextOptions<BusinessObjectsDbContext>>();
                services.RemoveAll<BusinessObjectsDbContext>();
                services.AddDbContext<BusinessObjectsDbContext>(opts =>
                    opts.UseNpgsql(_businessObjectsConnectionString));

                services.RemoveAll<DbContextOptions<RulesDbContext>>();
                services.RemoveAll<RulesDbContext>();
                services.AddDbContext<RulesDbContext>(opts =>
                    opts.UseNpgsql(_rulesConnectionString));

                services.RemoveAll<DbContextOptions<SolutionsDbContext>>();
                services.RemoveAll<SolutionsDbContext>();
                services.AddDbContext<SolutionsDbContext>(opts =>
                    opts.UseNpgsql(_solutionsConnectionString));

                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(_redis.GetConnectionString()));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(_emailCapture);
                services.AddSingleton<IEmailSender>(_emailCapture);

                services.RemoveAll<IProductAuthorizationService>();
                services.AddScoped<IProductAuthorizationService>(provider =>
                    new OptInProductAuthorizationService(
                        new ProductAuthorizationService(
                            provider.GetRequiredService<IAuthorizationSubjectActivity>(),
                            provider.GetRequiredService<IProductActionDescriptorRegistry>(),
                            provider.GetRequiredService<IProductPolicyReadStore>(),
                            provider.GetRequiredService<IAuthorizationAuditSink>(),
                            provider.GetRequiredService<IAuthorizationUnitOfWork>(),
                            provider.GetRequiredService<TimeProvider>()),
                        provider.GetRequiredService<IProductActionDescriptorRegistry>(),
                        _productAuthorizationTestAccess));

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
        Environment.SetEnvironmentVariable("ConnectionStrings__Authorization", _previousAuthorizationConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__BusinessObjects", _previousBusinessObjectsConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__Rules", _previousRulesConnectionStringEnv);
        Environment.SetEnvironmentVariable("ConnectionStrings__Solutions", _previousSolutionsConnectionStringEnv);
        Environment.SetEnvironmentVariable("Redis__ConnectionString", _previousRedisConnectionStringEnv);
    }

    public IServiceScope CreateScope() => _factory.Services.CreateScope();

    public HttpClient CreateAnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    public HttpClient CreateRawClient() => new(_factory.Server.CreateHandler())
    {
        BaseAddress = new Uri("https://localhost"),
    };

    public ApiTestHost CreateTestHost(
        RedisTicketStoreFailurePlan? redisFailurePlan = null,
        MutableTimeProvider? clock = null,
        TransitionReadRaceGate? transitionReadRaceGate = null)
    {
        WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ServiceDescriptor? trustedPublisherConfigurationDescriptor = services.FirstOrDefault(
                    descriptor => descriptor.ImplementationType == typeof(TrustedPublisherConfigurationService));
                if (trustedPublisherConfigurationDescriptor is not null)
                    services.Remove(trustedPublisherConfigurationDescriptor);

                if (clock is not null)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(clock);
                }

                if (redisFailurePlan is not null)
                {
                    services.RemoveAll<IConnectionMultiplexer>();
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                        FaultingRedisMultiplexer.Create(_redis.GetConnectionString(), redisFailurePlan));
                }

                if (transitionReadRaceGate is not null)
                {
                    services.RemoveAll<IWorkspaceContextTransitionRepository>();
                    services.AddScoped<IWorkspaceContextTransitionRepository>(sp =>
                        new RacingWorkspaceContextTransitionRepository(
                            new WorkspaceContextTransitionRepository(
                                sp.GetRequiredService<IdentityDbContext>()),
                            transitionReadRaceGate));
                }
            });
        });

        return new ApiTestHost(factory);
    }

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

    public async Task EnableProductAuthorizationTestAccessAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement session = await RefreshBrowserSecurityContextAsync(cancellationToken);
        JsonElement user = session.GetProperty("user");
        _productAuthorizationTestAccess.TryAdd(
            (user.GetProperty("workspaceId").GetGuid(), user.GetProperty("userId").GetGuid()),
            request => new ProductAuthorizationDecision(
                true,
                request.ActionKey.Contains(".record.", StringComparison.Ordinal)
                    ? ProductActionScope.All
                    : ProductActionScope.None));
    }

    public async Task SetProductAuthorizationTestDecisionAsync(
        Func<ProductAuthorizationRequest, ProductAuthorizationDecision> decide,
        CancellationToken cancellationToken = default)
    {
        JsonElement session = await RefreshBrowserSecurityContextAsync(cancellationToken);
        JsonElement user = session.GetProperty("user");
        _productAuthorizationTestAccess[
            (user.GetProperty("workspaceId").GetGuid(), user.GetProperty("userId").GetGuid())] = decide;
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

    private sealed class OptInProductAuthorizationService(
        IProductAuthorizationService inner,
        IProductActionDescriptorRegistry descriptors,
        ConcurrentDictionary<(Guid WorkspaceId, Guid SubjectId), Func<ProductAuthorizationRequest, ProductAuthorizationDecision>> access)
        : IProductAuthorizationService
    {
        public Task<ProductAuthorizationDecision> AuthorizeAsync(
            ProductAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            ProductActionDescriptor? descriptor = descriptors.Find(
                request.ActionKey,
                request.ResourceType);
            if (request.Subject.Kind == IdentitySubjectKind.Human
                && access.TryGetValue((request.WorkspaceId, request.Subject.Id), out Func<ProductAuthorizationRequest, ProductAuthorizationDecision>? decide)
                && descriptor is not null)
            {
                return Task.FromResult(decide(request));
            }

            return inner.AuthorizeAsync(request, cancellationToken);
        }
    }
}

[CollectionDefinition("Api")]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>;

public sealed class ApiTestHost(WebApplicationFactory<Program> factory) : IAsyncDisposable
{
    public HttpClient CreateRawClient() => new(factory.Server.CreateHandler())
    {
        BaseAddress = new Uri("https://localhost"),
    };

    public IServiceScope CreateScope() => factory.Services.CreateScope();

    public async Task<int> ExpireWorkspaceTransitionsAsync(CancellationToken cancellationToken)
    {
        WorkspaceTransitionExpiryService service = factory.Services
            .GetServices<IHostedService>()
            .OfType<WorkspaceTransitionExpiryService>()
            .Single();
        return await service.ExpireBatchAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();
}

public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now = _now.Add(duration);
}

public sealed class RedisTicketStoreFailurePlan
{
    private int _remainingTicketStoreFailures;

    public int TicketStoreFailures => Volatile.Read(ref _remainingTicketStoreFailures);

    public void FailNextTicketStoreWrite() => Interlocked.Increment(ref _remainingTicketStoreFailures);

    internal bool TryFailTicketStoreWrite(RedisKey key) =>
        key.ToString().StartsWith("axis:browser-session:", StringComparison.Ordinal)
        && Interlocked.CompareExchange(ref _remainingTicketStoreFailures, 0, 1) == 1;
}

public sealed class TransitionReadRaceGate
{
    private readonly TaskCompletionSource _bothReadsObserved = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _armed;
    private int _pendingReads;

    public void Arm() => Volatile.Write(ref _armed, 1);

    internal Task WaitForBothInitialReadsAsync()
    {
        if (Volatile.Read(ref _armed) == 0)
            return Task.CompletedTask;

        if (Interlocked.Increment(ref _pendingReads) == 2)
            _bothReadsObserved.TrySetResult();
        return _bothReadsObserved.Task;
    }
}

internal sealed class RacingWorkspaceContextTransitionRepository(
    IWorkspaceContextTransitionRepository inner,
    TransitionReadRaceGate gate) : IWorkspaceContextTransitionRepository
{
    private int _initialReads;

    public Task AddAsync(WorkspaceContextTransition transition, CancellationToken ct = default) =>
        inner.AddAsync(transition, ct);

    public async Task<WorkspaceContextTransition?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        WorkspaceContextTransition? transition = await inner.GetByIdAsync(id, ct);
        if (Interlocked.Increment(ref _initialReads) <= 2)
            await gate.WaitForBothInitialReadsAsync();
        return transition;
    }

    public Task<WorkspaceContextTransition?> GetBySourceCorrelationDigestAsync(
        Guid userId,
        string sourceCorrelationDigest,
        CancellationToken ct = default) =>
        inner.GetBySourceCorrelationDigestAsync(userId, sourceCorrelationDigest, ct);

    public Task<WorkspaceContextTransition?> GetByTargetCorrelationDigestAsync(
        Guid userId,
        string targetCorrelationDigest,
        CancellationToken ct = default) =>
        inner.GetByTargetCorrelationDigestAsync(userId, targetCorrelationDigest, ct);
}

internal static class FaultingRedisMultiplexer
{
    public static IConnectionMultiplexer Create(
        string connectionString,
        RedisTicketStoreFailurePlan failures)
    {
        IConnectionMultiplexer inner = ConnectionMultiplexer.Connect(connectionString);
        IDatabase database = DispatchProxy.Create<IDatabase, FaultingDatabaseProxy>();
        ((FaultingDatabaseProxy)(object)database).Initialize(inner.GetDatabase(), failures);
        IConnectionMultiplexer multiplexer =
            DispatchProxy.Create<IConnectionMultiplexer, FaultingMultiplexerProxy>();
        ((FaultingMultiplexerProxy)(object)multiplexer).Initialize(inner, database);
        return multiplexer;
    }

    private class FaultingMultiplexerProxy : DispatchProxy
    {
        private IConnectionMultiplexer _inner = null!;
        private IDatabase _database = null!;

        public void Initialize(IConnectionMultiplexer inner, IDatabase database)
        {
            _inner = inner;
            _database = database;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? _database
                : InvokeInner(_inner, targetMethod, args);
    }

    private class FaultingDatabaseProxy : DispatchProxy
    {
        private IDatabase _inner = null!;
        private RedisTicketStoreFailurePlan _failures = null!;

        public void Initialize(IDatabase inner, RedisTicketStoreFailurePlan failures)
        {
            _inner = inner;
            _failures = failures;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDatabase.StringSetAsync)
                && args is not null
                && args[0] is RedisKey key
                && _failures.TryFailTicketStoreWrite(key))
            {
                return Task.FromException<bool>(
                    new InvalidOperationException("Injected Redis ticket-store write failure."));
            }

            return InvokeInner(_inner, targetMethod, args);
        }
    }

    private static object? InvokeInner(object inner, MethodInfo? method, object?[]? args)
    {
        try
        {
            return method!.Invoke(inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
