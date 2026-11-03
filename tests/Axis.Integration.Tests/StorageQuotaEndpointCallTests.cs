using System.Net;
using System.Text;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The storage check of the inbound endpoint call, see <c>docs/reference/tenant-quotas.md#refusals</c>, on the EndpointApp
/// fixture served as a cell whose storage quota is 1000 bytes. Tenant <c>a</c> has a latest sample of 2000 bytes.
/// </summary>
public sealed class StorageQuotaEndpointCallTests(EndpointCallFixture fixture) : IClassFixture<EndpointCallFixture>
{
    private const string Call = "/api/apps/EndpointApp/endpoints/ImportNote";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Over_the_quota_an_endpoint_call_is_refused_and_writes_nothing_and_a_tenant_under_it_is_not()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.InCell(1000));
        using var client = host.CreateClient().CallingAs("importer-system");
        var notes = await fixture.CountAsync($"SELECT count(*) FROM {fixture.NoteTable}");
        var instances = await fixture.CountAsync("SELECT count(*) FROM axis.process_instances");
        var audits = await fixture.CountAsync("SELECT count(*) FROM axis.audit_records");

        await StorageQuotaHost.SeedUsageAsync(fixture.ConnectionString, "a", 2000, 0, CancellationToken);
        try
        {
            using var refused = await CallAsync(client, """{ "input": { "title": "Desk", "status": "open" } }""");
            using var malformed = await CallAsync(client, "{");

            using var problem = await ReadProblemAsync(refused, HttpStatusCode.Forbidden);
            Assert.Equal("This tenant has reached its storage quota.", problem.RootElement.GetProperty("title").GetString());
            Assert.Equal("quota-exceeded", problem.RootElement.GetProperty("reason").GetString());
            Assert.Equal("storage", problem.RootElement.GetProperty("quota").GetString());
            Assert.Equal(1000, problem.RootElement.GetProperty("limit").GetInt64());
            Assert.Equal(2000, problem.RootElement.GetProperty("used").GetInt64());
            using var malformedProblem = await ReadProblemAsync(malformed, HttpStatusCode.Forbidden);
            Assert.Equal("quota-exceeded", malformedProblem.RootElement.GetProperty("reason").GetString());
            Assert.Equal(notes, await fixture.CountAsync($"SELECT count(*) FROM {fixture.NoteTable}"));
            Assert.Equal(instances, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances"));
            Assert.Equal(audits, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records"));

            await StorageQuotaHost.SeedUsageAsync(fixture.ConnectionString, "a", 10, 0, CancellationToken);
            using var accepted = await CallAsync(client, """{ "input": { "title": "Desk", "status": "open" } }""");

            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }
        finally
        {
            await StorageQuotaHost.ClearUsageAsync(fixture.ConnectionString, CancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> CallAsync(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Call, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = EndpointCallFixture.Host;
        return await client.SendAsync(request, CancellationToken);
    }
}
