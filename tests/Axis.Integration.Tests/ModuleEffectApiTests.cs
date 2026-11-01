using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The record API treats the effect records of a transition like any other create. Approving a submitted claim creates
/// one entry for the signed-in user, writes its <c>record.created</c> audit record with the effect's point, and starts
/// the process of the create trigger on <c>Entry</c> for it. An effect that fails answers <c>409</c> with the fixed
/// title and the point, and writes nothing. A user without the <c>update</c> grant gets <c>403</c> and no entry.
/// </summary>
public sealed class ModuleEffectApiTests(ModuleEffectFixture fixture) : IClassFixture<ModuleEffectFixture>
{
    private const string Point = "Claim/effects/entryRecorded";
    private const string Claims = "/api/apps/ModuleEffectApp/entities/ledgers/Claim/records";
    private const string Approve = """{ "version": 1, "values": { "status": "approved" } }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_transition_through_the_record_API_audits_its_effect_record_as_a_create()
    {
        var claim = await fixture.InsertClaimAsync("submitted", 40);
        using var client = fixture.CreateClient().SignedInAs("clerk");

        using var response = await PatchAsync(client, claim, Approve);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(await fixture.EntryIdsAsync(claim));
        Assert.Equal([((long?)40, "clerk", "open")], await fixture.EntriesAsync(claim));
        var (actor, _, details) = Assert.Single(await fixture.AuditsAsync(entry), audit => audit.Action == "record.created");
        Assert.Equal("clerk", actor);
        using var parsed = JsonDocument.Parse(details);
        Assert.Equal(["effect", "version"], parsed.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(1, parsed.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(Point, parsed.RootElement.GetProperty("effect").GetString());
        Assert.Equal("record.updated", Assert.Single(await fixture.AuditsAsync(claim)).Action);
    }

    [Fact]
    public async Task A_create_trigger_on_the_effect_entity_starts_its_process_for_the_effect_record()
    {
        var claim = await fixture.InsertClaimAsync("submitted", 40);
        using var client = fixture.CreateClient().SignedInAs("clerk");

        using var response = await PatchAsync(client, claim, Approve);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = Assert.Single(await fixture.EntryIdsAsync(claim));
        Assert.Equal(1, await fixture.InstanceCountAsync(entry));
        Assert.Equal(0, await fixture.InstanceCountAsync(claim));
    }

    [Fact]
    public async Task A_failing_effect_is_409_with_the_point_and_writes_nothing()
    {
        var claim = await fixture.InsertClaimAsync("submitted", -1);
        var instances = await fixture.InstanceCountAsync();
        using var client = fixture.CreateClient().SignedInAs("clerk");

        using var response = await PatchAsync(client, claim, Approve);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("A required change could not be recorded.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(Point, problem.RootElement.GetProperty("point").GetString());
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(("submitted", 1), await fixture.ClaimAsync(claim));
        Assert.Empty(await fixture.EntryIdsAsync(claim));
        Assert.Empty(await fixture.AuditsAsync(claim));
        Assert.Equal(instances, await fixture.InstanceCountAsync());
    }

    [Fact]
    public async Task A_user_without_the_update_grant_is_403_and_no_effect_record_is_created()
    {
        var claim = await fixture.InsertClaimAsync("submitted", 40);
        using var client = fixture.CreateClient().SignedInAs("outsider");

        using var response = await PatchAsync(client, claim, Approve);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(("submitted", 1), await fixture.ClaimAsync(claim));
        Assert.Empty(await fixture.EntryIdsAsync(claim));
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid claim, string body) =>
        client.SendAsync(Request(HttpMethod.Patch, $"{Claims}/{claim:D}", ModuleEffectFixture.Host, body), CancellationToken);
}
