using System.Net;
using System.Security.Cryptography;
using System.Text;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.SignUp;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The refresh of the disposable mailbox domains on a real PostgreSQL, against a download that a message handler stands in for, see
/// <c>docs/reference/sign-up.md#bots-and-abuse</c>. Each test class instance has a container of its own, seeded with <c>old.example</c>.
/// </summary>
public sealed class DisposableDomainRefreshTests : IAsyncLifetime
{
    private const string List = "# comment\n\nMailinator.com\r\nyopmail.com\nYOPMAIL.com\n";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly LogCollector _logs = new();
    private readonly ListHandler _handler = new();
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), TestContext.Current.CancellationToken);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        await ControlPlaneDatabase.ExecuteAsync(_dataSource, "INSERT INTO control_plane.disposable_domains (domain) VALUES ('old.example')", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _loggers.Dispose();
        _handler.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_list_with_the_pinned_hash_replaces_the_table_with_its_domains_in_lower_case_without_comments_blanks_or_repeats()
    {
        _handler.Body = List;

        await RunAsync(Sha256Of(List));

        Assert.Equal(["mailinator.com", "yopmail.com"], await DomainsAsync());
        Assert.DoesNotContain(_logs.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_list_with_another_hash_keeps_the_table_and_logs_an_error()
    {
        _handler.Body = List;

        await RunAsync(Sha256Of("another list\n"));

        Assert.Equal(["old.example"], await DomainsAsync());
        var error = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("ControlPlane:SignUp:DisposableDomains:Sha256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_that_answers_an_error_throws_and_keeps_the_table()
    {
        _handler.Status = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(Sha256Of(List)));

        Assert.Equal(["old.example"], await DomainsAsync());
    }

    private Task RunAsync(string sha256)
    {
        var options = new ControlPlaneOptions();
        options.SignUp.DisposableDomains.Url = "https://lists.example/disposable.conf";
        options.SignUp.DisposableDomains.Sha256 = sha256;
        var refresh = new DisposableDomainRefresh(_dataSource, new HandlerFactory(_handler), options, _loggers.CreateLogger<DisposableDomainRefresh>());
        return refresh.RunAsync(new ClaimedJob(Guid.NewGuid(), DisposableDomainRefresh.JobKind, [], 1), TestContext.Current.CancellationToken);
    }

    private static string Sha256Of(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private async Task<string[]> DomainsAsync()
    {
        await using var command = _dataSource.CreateCommand("SELECT domain FROM control_plane.disposable_domains ORDER BY domain");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Stands in for the host of the list: it answers the set status, with the set body when the status is a success.</summary>
    private sealed class ListHandler : HttpMessageHandler
    {
        public string Body { get; set; } = string.Empty;

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8) });
    }
}
