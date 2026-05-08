using System.Text;
using System.Text.Json;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

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

    public HttpClient Client { get; private set; } = null!;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:DataModeling"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:WorkflowBuilder"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:FormBuilder"] = _postgres.GetConnectionString(),
                    ["ConnectionStrings:WorkflowEngine"] = _postgres.GetConnectionString(),
                    ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                    ["Jwt:SecretKey"] = "test-secret-key-that-is-long-enough-32chars!!",
                    ["Jwt:Issuer"] = "axis-test",
                    ["Jwt:Audience"] = "axis-test",
                    ["Jwt:AccessTokenTtlMinutes"] = "15",
                    ["RefreshToken:TtlDays"] = "7",
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Replace IdentityDbContext with test container
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.AddDbContext<IdentityDbContext>(opts =>
                    opts.UseNpgsql(_postgres.GetConnectionString()));

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
                // which is not registered; domain events are irrelevant in tests
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(sp =>
                    new NullUnitOfWork(sp.GetRequiredService<IdentityDbContext>()));

                // Re-configure JWT bearer to use test signing key.
                // Program.cs captures builder.Configuration["Jwt:SecretKey"] at startup time
                // (resolves to appsettings.json value). JwtTokenService reads IConfiguration at
                // runtime (resolves to in-memory override). PostConfigure aligns them.
                const string TestJwtKey = "test-secret-key-that-is-long-enough-32chars!!";
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
                    opts.TokenValidationParameters.ValidIssuer = "axis-test";
                    opts.TokenValidationParameters.ValidAudience = "axis-test";
                });
            });
        });

        using var scope = _factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await ctx.Database.EnsureCreatedAsync();

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
}

[CollectionDefinition("Api")]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>;
