using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A retry of a failed instance runs old steps again. It is refused when a migration applied since the
/// instance's pinned release dropped a column the release uses. Tenant <c>a</c> holds the refused retry
/// and tenant <c>b</c> the allowed one, so the tests stay independent.
/// </summary>
public sealed class ProcessRetryMigrationTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Reason = """{ "reason": "Fixed the record" }""";

    private const string ScratchField = "{ \"name\": \"scratch\", \"type\": \"text\" },";

    private static readonly string _requestPath = Path.Combine("entities", "request.json");

    private static readonly string _financePath = Path.Combine("policies", "finance-access.json");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_retry_is_refused_when_an_applied_drop_field_removed_a_column_the_pinned_release_uses_and_nothing_is_written()
    {
        var withScratch = await fixture.ActivateEditedAsync(TenantA, AddScratch);
        var instance = await FailAsync(TenantA, withScratch);
        await fixture.ActivateEditedAsync(TenantA, AllowRetry, added: DropScratch());
        var before = await fixture.SnapshotAsync(TenantA, instance);
        var history = await fixture.HistoryAsync(TenantA, instance);
        Assert.Equal("failed", before.State);

        using var response = await RetryAsync(TenantA, instance);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("A migration changed a column that the instance's release uses.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal(before, await fixture.SnapshotAsync(TenantA, instance));
        Assert.Equal(history, await fixture.HistoryAsync(TenantA, instance));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, instance, "process.retried"));
    }

    [Fact]
    public async Task A_retry_succeeds_when_an_applied_drop_field_removed_a_column_the_pinned_release_does_not_have()
    {
        var first = fixture.ReleaseId(TenantB);
        var instance = await FailAsync(TenantB, first);
        await fixture.ActivateEditedAsync(TenantB, AddScratch);
        await fixture.ActivateEditedAsync(TenantB, AllowRetry, added: DropScratch());

        using var response = await RetryAsync(TenantB, instance);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("running", (await fixture.SnapshotAsync(TenantB, instance)).State);
    }

    private static string? AddScratch(string path, string content) =>
        path == _requestPath
            ? content.Replace("\"fields\": [", $"\"fields\": [\n    {ScratchField}", StringComparison.Ordinal)
            : AllowRetry(path, content);

    // The retry grant is in every release the tests activate, since the grant comes from the active release.
    private static string? AllowRetry(string path, string content) =>
        path == _financePath
            ? content.Replace(
                "{ \"name\": \"decideStart\", \"process\": \"Decide\", \"actions\": [\"start\"] },",
                "{ \"name\": \"decideStart\", \"process\": \"Decide\", \"actions\": [\"start\"] },\n    { \"process\": \"Divide\", \"actions\": [\"retry\"] },",
                StringComparison.Ordinal)
            : content;

    private static Dictionary<string, string> DropScratch() =>
        new Dictionary<string, string>
        {
            ["migrations/0001-drop-scratch.json"] =
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "DropScratch", "formatVersion": 1, "entity": "Request",
                  "changes": [{ "type": "dropField", "field": "scratch" }] }
                """,
        };

    // A request with a zero divisor makes the Divide decision fail, so the instance ends up failed.
    private async Task<Guid> FailAsync(string tenant, Guid release)
    {
        var subject = await fixture.InsertRequestAsync(tenant, null, divisor: 0, amount: 10m);
        var instance = await fixture.StartAsync(tenant, "Divide", subject, release);
        await fixture.RunUntilIdleAsync(tenant, CancellationToken);
        Assert.Equal("failed", (await fixture.SnapshotAsync(tenant, instance)).State);
        return instance;
    }

    private async Task<HttpResponseMessage> RetryAsync(string tenant, Guid instance)
    {
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var request = RecordApiFixture.Request(
            HttpMethod.Post, $"/api/apps/StepApp/processes/Divide/instances/{instance:D}/retry", Host(tenant), Reason);
        return await maria.SendAsync(request, CancellationToken);
    }
}
