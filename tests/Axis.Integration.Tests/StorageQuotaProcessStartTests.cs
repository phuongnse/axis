using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A process start is not refused over the storage quota, see <c>docs/reference/tenant-quotas.md#refusals</c>. Process steps
/// run in the worker and never pass through a route, so the start is the route that writes the instance and queues its first
/// step. The ProcessApp fixture is served as a cell whose storage quota is 1000 bytes, and tenant <c>a</c> has a latest
/// sample of 2000 bytes.
/// </summary>
public sealed class StorageQuotaProcessStartTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string Instances = "/api/apps/ProcessApp/processes/Submit/instances";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Over_the_quota_a_process_start_still_writes_its_instance_and_queues_its_first_step()
    {
        var subject = await fixture.InsertRequestAsync(status: null);
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.InCell(1000));
        using var client = host.CreateClient().SignedInAs("requester");
        await StorageQuotaHost.SeedUsageAsync(fixture.ConnectionString, "a", 2000, 0, CancellationToken);
        try
        {
            using var request = Request(HttpMethod.Post, Instances, ProcessStartFixture.Host, $$"""{ "subjectId": "{{subject:D}}" }""");
            using var response = await client.SendAsync(request, CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("id").GetGuid();
            Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE id = @id", ("id", id)));
            Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = 'process.step'", ("id", id)));
        }
        finally
        {
            await StorageQuotaHost.ClearUsageAsync(fixture.ConnectionString, CancellationToken);
        }
    }
}
