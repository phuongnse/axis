using System.Net;
using System.Text.Json;
using Axis.Core.Tenancy;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class WorkerHealthTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string BlockKind = "test.block";
    private const string WaitingLog = "Information: Waiting for the database of tenant a to be migrated.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ready_is_unhealthy_until_the_tenant_database_is_migrated_and_healthy_after()
    {
        var connectionString = await database.CreateDatabaseAsync("worker_health_ready", CancellationToken);
        var logs = new LogCollector();
        await using var app = BuildWorker(logs, connectionString);
        await app.StartAsync(CancellationToken);
        using var client = ClientOf(app);
        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Contains(WaitingLog)), "The worker did not wait for the migrations.");

        var before = await GetAsync(client, "/health/ready");
        var live = await GetAsync(client, "/health/live");

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), before);
        Assert.Equal((HttpStatusCode.OK, "Healthy"), live);

        await MigrateAsync(connectionString);

        await WaitUntilAsync(async () => await GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Healthy"), "The worker did not become ready.");
    }

    [Fact]
    public async Task Live_and_ready_turn_unhealthy_when_a_work_item_never_finishes_within_twice_the_lease()
    {
        var connectionString = await database.CreateDatabaseAsync("worker_health_stall", CancellationToken);
        await MigrateAsync(connectionString);
        var handler = new BlockingHandler();
        await using var app = BuildWorker(new LogCollector(), connectionString, handler, ("Worker:LeaseDuration", "00:00:01"));
        await app.StartAsync(CancellationToken);
        using var client = ClientOf(app);
        await WaitUntilAsync(async () => await GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Healthy"), "The worker did not become ready.");

        await EnqueueAsync(connectionString);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        await WaitUntilAsync(
            async () => await GetAsync(client, "/health/live") == (HttpStatusCode.ServiceUnavailable, "Unhealthy"),
            "The loop did not count as stalled within 15 seconds.",
            TimeSpan.FromSeconds(15));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), await GetAsync(client, "/health/ready"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/api/me")]
    public async Task Any_other_path_answers_not_found(string path)
    {
        var connectionString = await database.CreateDatabaseAsync("worker_health_not_found", CancellationToken);
        await using var app = BuildWorker(new LogCollector(), connectionString);
        await app.StartAsync(CancellationToken);
        using var client = ClientOf(app);

        using var response = await client.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplication BuildWorker(
        LogCollector logs,
        string connectionString,
        BlockingHandler? handler = null,
        params (string Key, string Value)[] extraSettings)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?>
        {
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
        };
        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        if (handler is not null)
        {
            builder.Services.AddSingleton<IWorkItemHandler>(handler);
        }

        var app = builder.Build();
        app.MapWorkerHealth();
        return app;
    }

    private static HttpClient ClientOf(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<(HttpStatusCode Status, string? Health)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        // The answer holds only the overall status.
        Assert.Equal(["status"], body.RootElement.EnumerateObject().Select(property => property.Name));
        return (response.StatusCode, body.RootElement.GetProperty("status").GetString());
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        await context.Database.MigrateAsync(CancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
    }

    private static async Task EnqueueAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);

        // Due a minute ago, so a small clock difference with the database does not delay the claim.
        await WorkItemQueue.EnqueueAsync(connection, null, "a", BlockKind, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);
        }
    }

    /// <summary>Starts and then waits until the worker stops, so the loop never returns to polling.</summary>
    private sealed class BlockingHandler : IWorkItemHandler
    {
        public string Kind => BlockKind;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
