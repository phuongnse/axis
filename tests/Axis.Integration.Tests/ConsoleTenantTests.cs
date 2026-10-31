using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ConsoleTenantTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Path = "/api/console/tenant";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tenant_is_refused_without_a_session()
    {
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, HostA);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_is_refused_to_a_user_without_the_operator_role()
    {
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs("requester");

        using var response = await SendAsync(client, HostA);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_is_refused_to_everyone_when_the_operator_role_is_not_set()
    {
        using var client = fixture.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(client, HostA);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_answers_the_operator_the_active_applications_of_the_host()
    {
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(client, HostA);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var tenant = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(TenantA, tenant.RootElement.GetProperty("id").GetString());
        Assert.Equal("active", tenant.RootElement.GetProperty("status").GetString());
        var expected = await ActiveReleasesAsync(TenantA);
        Assert.Equal(3, expected.Count);
        var applications = tenant.RootElement.GetProperty("applications").EnumerateArray().ToList();
        Assert.Equal(
            expected.Select(row => (row.ApplicationId, row.Name, row.ReleaseId)),
            applications.Select(row => (row.GetProperty("id").GetGuid(), row.GetProperty("name").GetString()!, row.GetProperty("releaseId").GetGuid())));
        Assert.DoesNotContain(applications, row => row.GetProperty("name").GetString() == fixture.DormantApp);
        Assert.All(applications, row =>
        {
            Assert.Equal(1, row.GetProperty("releaseNumber").GetInt32());
            Assert.True(row.TryGetProperty("releaseLabel", out _));
            Assert.True(row.TryGetProperty("activatedAt", out _));
        });
    }

    [Fact]
    public async Task Tenant_answers_only_the_tenant_of_the_host()
    {
        await using var factory = OperatorRoleIsReader();
        using var client = factory.CreateClient().SignedInAs("rita");

        using var response = await SendAsync(client, HostB);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var tenant = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(TenantB, tenant.RootElement.GetProperty("id").GetString());
        var expected = Assert.Single(await ActiveReleasesAsync(TenantB));
        var application = Assert.Single(tenant.RootElement.GetProperty("applications").EnumerateArray());
        Assert.Equal(expected.ApplicationId, application.GetProperty("id").GetGuid());
        Assert.Equal(expected.ReleaseId, application.GetProperty("releaseId").GetGuid());
    }

    private WebApplicationFactory<Program> OperatorRoleIsReader() =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Console:OperatorRole", "reader"));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string host)
    {
        using var request = Request(Path, host);
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<IReadOnlyList<ActiveRelease>> ActiveReleasesAsync(string tenant)
    {
        await using var configuration = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(fixture.ConnectionString(tenant)).Options);
        return await new ActiveReleaseStore(configuration).ListAsync(CancellationToken);
    }
}
