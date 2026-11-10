using System.Security.Cryptography.X509Certificates;
using Axis.ControlPlane.Cells;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The cell client against <see cref="CellTestServer"/>, a Kestrel server that requires a client certificate.</summary>
public sealed class CellClientTests : IAsyncLifetime
{
    private static readonly CellClientOptions _fast = new()
    {
        PollDelay = TimeSpan.FromMilliseconds(10),
        MaxPollDelay = TimeSpan.FromMilliseconds(40),
        FollowTimeout = TimeSpan.FromSeconds(30),
    };

    private readonly List<ServiceProvider> _providers = [];
    private CellTestServer _server = null!;

    public async ValueTask InitializeAsync() => _server = await CellTestServer.StartAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    [Fact]
    public async Task The_client_presents_its_certificate_and_a_bearer_token_over_https()
    {
        var clients = Build();
        var cell = clients.Get("vn-1");

        var job = await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, TestContext.Current.CancellationToken);

        var seen = Assert.Single(_server.Posts);
        Assert.Equal("Bearer token-1", seen.Authorization);
        Assert.NotNull(seen.ClientCertificateThumbprint);
        Assert.Equal(CellJob.Queued, job.State);
        Assert.Equal("acme", job.TenantId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), job.Id);
    }

    [Fact]
    public async Task The_token_request_signs_in_with_client_credentials_and_the_token_is_cached()
    {
        var clients = Build();
        var cell = clients.Get("vn-1");
        var token = TestContext.Current.CancellationToken;

        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, token);
        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, token);

        Assert.Equal(1, _server.TokenCount);
        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("axis-controlplane:cell-secret"));
        Assert.Equal($"Basic {expected}", Assert.Single(_server.TokenRequests));
    }

    [Fact]
    public async Task A_token_that_ends_within_the_refresh_margin_is_not_kept()
    {
        _server.TokenLifetimeSeconds = 30;
        var clients = Build();
        var cell = clients.Get("vn-1");
        var token = TestContext.Current.CancellationToken;

        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, token);
        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, token);

        Assert.Equal(2, _server.TokenCount);
        Assert.Equal(["Bearer token-1", "Bearer token-2"], _server.Posts.Select(p => p.Authorization));
    }

    [Fact]
    public async Task A_client_without_the_certificate_fails_and_the_endpoint_never_runs()
    {
        // It trusts the server's authority, so the only thing missing is its own certificate.
        var authorities = new X509Certificate2Collection();
        authorities.ImportFromPemFile(_server.CertificateAuthorityPath);
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
        policy.CustomTrustStore.AddRange(authorities);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        handler.SslOptions.CertificateChainPolicy = policy;
        using var http = new HttpClient(handler);
        using var cell = new CellClient(Cell(), "cell-secret", () => http, _fast, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<CellCallException>(() =>
            cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("cell-secret", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_server.Posts);
        Assert.Equal(0, _server.TokenCount);
    }

    [Fact]
    public async Task A_certificate_of_another_authority_is_not_trusted_when_the_authority_is_set()
    {
        // The chain policy trusts only the region's authority, so a server that chains elsewhere is refused.
        await using var other = await CellTestServer.StartAsync();
        var options = Options(other.InternalUrl, other.TokenUrl, other.CertificatePath, other.KeyPath, _server.CertificateAuthorityPath);
        var cell = Build(options: options).Get("vn-1");

        await Assert.ThrowsAsync<CellCallException>(() =>
            cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, TestContext.Current.CancellationToken));

        Assert.Empty(other.Posts);
    }

    [Fact]
    public void An_http_address_is_never_called()
    {
        using var http = new HttpClient();
        var cell = new CellOptions { Id = "vn-1", InternalUrl = "http://localhost:1", TokenUrl = _server.TokenUrl.ToString(), ClientId = "axis-controlplane" };

        var exception = Assert.Throws<ArgumentException>(() => new CellClient(cell, "cell-secret", () => http, _fast, TimeProvider.System));

        Assert.DoesNotContain("cell-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_path_that_leaves_the_cell_is_refused_before_anything_is_sent()
    {
        var clients = Build();
        var cell = clients.Get("vn-1");

        await Assert.ThrowsAsync<CellCallException>(() =>
            cell.PostAsync("https://other.example/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, TestContext.Current.CancellationToken));

        Assert.Empty(_server.Posts);
        Assert.Equal(0, _server.TokenCount);
    }

    [Fact]
    public async Task A_retried_post_sends_the_same_idempotency_key_and_another_record_sends_another()
    {
        var clients = Build();
        var cell = clients.Get("vn-1");
        var token = TestContext.Current.CancellationToken;
        var record = Guid.NewGuid();
        var body = new { reason = "unpaid" };

        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", record), body, token);
        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", record), body, token);
        await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), body, token);

        var keys = _server.Posts.Select(p => p.IdempotencyKey).ToList();
        Assert.Equal($"tenant-suspend:{record:D}", keys[0]);
        Assert.Equal(keys[0], keys[1]);
        Assert.NotEqual(keys[0], keys[2]);
        Assert.All(_server.Posts, p => Assert.Equal("{\"reason\":\"unpaid\"}", p.Body));
    }

    [Fact]
    public async Task Following_a_job_returns_it_when_it_succeeds()
    {
        _server.ScriptJob("queued", "running", "succeeded");
        var clients = Build();
        var cell = clients.Get("vn-1");
        var id = Guid.NewGuid();

        var job = await cell.FollowAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(CellJob.Succeeded, job.State);
        Assert.Equal(id, job.Id);
        Assert.Equal(3, _server.JobReads);
        var step = Assert.Single(job.Steps!);
        Assert.Equal("succeeded", step.State);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Following_a_job_returns_it_with_its_steps_and_error_when_it_fails()
    {
        _server.ScriptJob("running", "failed");
        var clients = Build();
        var cell = clients.Get("vn-1");

        var job = await cell.FollowAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(CellJob.Failed, job.State);
        Assert.Equal("The tenant is still provisioning.", job.Error);
        var step = Assert.Single(job.Steps!);
        Assert.Equal(new CellJobStep("status", "failed", "The tenant is still provisioning."), step);
    }

    [Fact]
    public async Task Following_a_job_that_never_ends_throws_after_the_follow_timeout()
    {
        _server.ScriptJob("running");
        var clients = Build(new CellClientOptions
        {
            PollDelay = TimeSpan.FromMilliseconds(10),
            MaxPollDelay = TimeSpan.FromMilliseconds(20),
            FollowTimeout = TimeSpan.FromMilliseconds(200),
        });
        var cell = clients.Get("vn-1");

        await Assert.ThrowsAsync<TimeoutException>(() => cell.FollowAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_tenant_list_holds_each_tenant_id_and_hosts_in_the_order_of_the_cell()
    {
        _server.Tenants = [("acme", ["acme.vn.example.com", "www.acme.example"]), ("beta", [])];
        var cell = Build().Get("vn-1");

        var tenants = await cell.ListTenantsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["acme", "beta"], tenants.Select(t => t.Id));
        Assert.Equal(["acme.vn.example.com", "www.acme.example"], tenants[0].Hosts);
        Assert.Empty(tenants[1].Hosts);
        Assert.Equal(1, _server.TokenCount);
    }

    [Fact]
    public async Task A_tenant_list_answered_with_an_error_throws_with_the_status_and_no_body()
    {
        _server.TenantListStatus = 500;
        var cell = Build().Get("vn-1");

        var exception = await Assert.ThrowsAsync<CellCallException>(() => cell.ListTenantsAsync(TestContext.Current.CancellationToken));

        Assert.Equal(500, exception.Status);
        Assert.Contains("vn-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_sends_the_idempotency_key_and_no_body_and_returns_the_queued_job()
    {
        var cell = Build().Get("vn-1");
        var record = Guid.NewGuid();

        var job = await cell.DeleteAsync("/api/cell/tenants/acme", new CellOperation("delete", record), TestContext.Current.CancellationToken);

        var seen = Assert.Single(_server.Deletes);
        Assert.Equal("Bearer token-1", seen.Authorization);
        Assert.Equal($"delete:{record:D}", seen.IdempotencyKey);
        Assert.Equal(string.Empty, seen.Body);
        Assert.NotNull(seen.ClientCertificateThumbprint);
        Assert.Equal(CellJob.Queued, job.State);
        Assert.Equal("acme", job.TenantId);
    }

    [Fact]
    public async Task A_tenant_read_returns_the_status_and_a_tenant_the_cell_does_not_know_is_null()
    {
        var cell = Build().Get("vn-1");
        var token = TestContext.Current.CancellationToken;
        _server.TenantStatus = "deleting";

        Assert.Equal("deleting", await cell.GetTenantStatusAsync("acme", token));

        _server.TenantStatus = null;
        Assert.Null(await cell.GetTenantStatusAsync("acme", token));
    }

    [Fact]
    public async Task A_tenant_read_answered_with_an_error_throws_with_the_status_and_no_body()
    {
        _server.TenantReadStatus = 500;
        var cell = Build().Get("vn-1");

        var exception = await Assert.ThrowsAsync<CellCallException>(() => cell.GetTenantStatusAsync("acme", TestContext.Current.CancellationToken));

        Assert.Equal(500, exception.Status);
        Assert.Contains("vn-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_a_401_the_client_gets_one_new_token_and_sends_the_call_again()
    {
        var clients = Build();
        var cell = clients.Get("vn-1");
        _server.RefuseNext(1);

        var job = await cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, TestContext.Current.CancellationToken);

        Assert.Equal(CellJob.Queued, job.State);
        Assert.Equal(2, _server.TokenCount);
        Assert.Equal("Bearer token-2", Assert.Single(_server.Posts).Authorization);
    }

    [Fact]
    public async Task A_second_401_throws_after_one_retry()
    {
        var clients = Build();
        var cell = clients.Get("vn-1");
        _server.RefuseNext(100);

        var exception = await Assert.ThrowsAsync<CellCallException>(() =>
            cell.PostAsync("/api/cell/tenants/acme/suspend", new CellOperation("tenant-suspend", Guid.NewGuid()), null, TestContext.Current.CancellationToken));

        Assert.Equal(401, exception.Status);
        Assert.Contains("vn-1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, _server.TokenCount);
        Assert.Empty(_server.Posts);
    }

    [Fact]
    public async Task A_409_titled_this_cell_is_full_sets_the_full_flag()
    {
        _server.ConflictTitle = CellClient.FullTitle;
        var cell = Build().Get("vn-1");

        var exception = await Assert.ThrowsAsync<CellCallException>(() =>
            cell.PostAsync("/api/cell/tenants", new CellOperation("tenant-create", Guid.NewGuid()), new { id = "acme" }, TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.Status);
        Assert.True(exception.CellFull);
        Assert.Contains("vn-1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(CellClient.FullTitle, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_409_with_another_title_does_not_set_the_full_flag()
    {
        _server.ConflictTitle = "The tenant id is taken.";
        var cell = Build().Get("vn-1");

        var exception = await Assert.ThrowsAsync<CellCallException>(() =>
            cell.PostAsync("/api/cell/tenants", new CellOperation("tenant-create", Guid.NewGuid()), new { id = "acme" }, TestContext.Current.CancellationToken));

        Assert.Equal(409, exception.Status);
        Assert.False(exception.CellFull);
        Assert.DoesNotContain("The tenant id is taken.", exception.Message, StringComparison.Ordinal);
    }

    private CellClients Build(CellClientOptions? clientOptions = null, ControlPlaneOptions? options = null)
    {
        options ??= Options(_server.InternalUrl, _server.TokenUrl, _server.CertificatePath, _server.KeyPath, _server.CertificateAuthorityPath);
        var services = new ServiceCollection().AddSingleton(options).AddSingleton(clientOptions ?? _fast).AddCellClients(options);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider.GetRequiredService<CellClients>();
    }

    private static ControlPlaneOptions Options(Uri internalUrl, Uri tokenUrl, string certificate, string key, string authority)
    {
        var options = new ControlPlaneOptions
        {
            CellClientCertificate = new CellClientCertificateOptions { Path = certificate, KeyPath = key },
            CellCertificateAuthorityPath = authority,
        };
        options.Cells.Add(new CellOptions { Id = "vn-1", InternalUrl = internalUrl.ToString(), TokenUrl = tokenUrl.ToString(), ClientId = "axis-controlplane" });
        options.CellSecrets["vn-1"] = "cell-secret";
        return options;
    }

    private CellOptions Cell() => new() { Id = "vn-1", InternalUrl = _server.InternalUrl.ToString(), TokenUrl = _server.TokenUrl.ToString(), ClientId = "axis-controlplane" };
}
