using System.Net;
using System.Text.Json;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// ModuleRelaxApp relaxes one point of each of four modules, so the engine stops applying it: the <c>requests</c>
/// <c>Request</c> submit guard, the <c>stages</c> <c>Ticket</c> <c>amountFrozenAfterSubmit</c> validation, the
/// <c>ledgers</c> <c>Claim</c> <c>entryRecorded</c> effect and the <c>claims</c> <c>viewer</c> ceiling slot. ModuleContractApp
/// pins the same <c>requests</c> module without a relax entry and keeps refusing the write.
/// </summary>
public sealed class ModuleRelaxTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string Clerk = "relax-clerk";
    private const string Auditor = "relax-auditor";
    private const string Requests = "/api/apps/ModuleRelaxApp/entities/requests/Request/records";
    private const string UnrelaxedRequests = "/api/apps/ModuleContractApp/entities/requests/Request/records";
    private const string Tickets = "/api/apps/ModuleRelaxApp/entities/stages/Ticket/records";
    private const string LedgerClaims = "/api/apps/ModuleRelaxApp/entities/ledgers/Claim/records";
    private const string Entries = "/api/apps/ModuleRelaxApp/entities/ledgers/Entry/records";
    private const string Claims = "/api/apps/ModuleRelaxApp/entities/claims/Claim/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_relaxed_transition_guard_lets_a_request_with_amount_0_go_from_draft_to_submitted_where_the_unrelaxed_module_refuses()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var refused = await CreateAsync(requester, UnrelaxedRequests, """{ "title": "Refused", "amount": 0, "status": "draft" }""");
        var relaxed = await CreateAsync(requester, Requests, """{ "title": "Relaxed", "amount": 0, "status": "draft" }""");

        using var refusal = await SendAsync(requester, HttpMethod.Patch, $"{UnrelaxedRequests}/{refused.Id}", Patch(refused.Version, """{ "status": "submitted" }"""));
        using var written = await SendAsync(requester, HttpMethod.Patch, $"{Requests}/{relaxed.Id}", Patch(relaxed.Version, """{ "status": "submitted" }"""));

        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        using var body = JsonDocument.Parse(await written.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("submitted", body.RootElement.GetProperty("values").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_relaxed_core_validation_lets_the_amount_of_a_submitted_ticket_change()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var ticket = await CreateAsync(requester, Tickets, """{ "title": "Ticket", "amount": 5, "status": "draft" }""");
        var submitted = await PatchAsync(requester, Tickets, ticket.Id, ticket.Version, """{ "status": "submitted" }""");

        using var response = await SendAsync(requester, HttpMethod.Patch, $"{Tickets}/{ticket.Id}", Patch(submitted, """{ "amount": 7 }"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(7, body.RootElement.GetProperty("values").GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task A_relaxed_effect_creates_no_entry_when_a_claim_is_approved()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var claim = await CreateAsync(clerk, LedgerClaims, """{ "title": "Claim", "amount": 5, "status": "draft" }""");
        var submitted = await PatchAsync(clerk, LedgerClaims, claim.Id, claim.Version, """{ "status": "submitted" }""");

        var approved = await PatchAsync(clerk, LedgerClaims, claim.Id, submitted, """{ "status": "approved" }""");

        Assert.True(approved > submitted);
        using var entries = await GetJsonAsync(clerk, $"{Entries}?pageSize=100");
        var claims = entries.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("values").GetProperty("claim").GetString())
            .ToList();
        Assert.DoesNotContain(claim.Id.ToString(), claims);
    }

    [Fact]
    public async Task A_grant_past_a_relaxed_ceiling_slot_lists_reads_and_updates_the_claims_of_other_requesters()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var other = await CreateAsync(clerk, Claims, $$"""{ "title": "Other", "code": "c{{Guid.NewGuid():N}}", "requester": "somebody-else" }""");
        using var auditor = fixture.CreateClient().SignedInAs(Auditor);

        using var list = await GetJsonAsync(auditor, $"{Claims}?pageSize=100");
        using var read = await SendAsync(auditor, HttpMethod.Get, $"{Claims}/{other.Id}");
        using var updated = await SendAsync(auditor, HttpMethod.Patch, $"{Claims}/{other.Id}", Patch(other.Version, """{ "title": "Changed" }"""));

        var listed = list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(other.Id, listed);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var body = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("Changed", body.RootElement.GetProperty("values").GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_role_without_a_create_grant_cannot_create_a_claim_whatever_the_relaxed_ceiling_allows()
    {
        using var auditor = fixture.CreateClient().SignedInAs(Auditor);

        using var response = await SendAsync(
            auditor, HttpMethod.Post, Claims, $$"""{ "values": { "title": "Denied", "code": "c{{Guid.NewGuid():N}}", "requester": "{{Auditor}}" } }""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string Patch(long version, string values) => $$"""{ "version": {{version}}, "values": {{values}} }""";

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        using var request = RecordApiFixture.Request(method, path, ModuleAppFixture.Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task<(Guid Id, long Version)> CreateAsync(HttpClient client, string records, string values)
    {
        using var response = await SendAsync(client, HttpMethod.Post, records, $$"""{ "values": {{values}} }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    // Updates the record and returns its new version.
    private static async Task<long> PatchAsync(HttpClient client, string records, Guid id, long version, string values)
    {
        using var response = await SendAsync(client, HttpMethod.Patch, $"{records}/{id}", Patch(version, values));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("version").GetInt64();
    }
}
