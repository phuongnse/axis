using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The core validations of the <c>stages</c> module's <c>Ticket</c> contract run on every write, after the entity's own
/// validation <c>amountNotNegative</c> and before the lifecycle check. <c>amountFrozenAfterSubmit</c> refuses a change of
/// <c>amount</c> once the ticket left <c>draft</c>, with a <c>400</c> at <c>/values/amount</c> and the record unchanged,
/// and allows the same change in <c>draft</c>. A write that breaks both the entity's validation and the core validation
/// gets only the entity's message. <c>stepMovesWithNewDecision</c> refuses the second <c>updateRecord</c> step that
/// reuses <c>decisionTask()</c> to move <c>step</c>, which fails the instance and keeps the first step's value.
/// </summary>
public sealed class ModuleCoreValidationTests(ModuleCoreValidationFixture fixture) : IClassFixture<ModuleCoreValidationFixture>
{
    private const string App = "/api/apps/ModuleCoreValidationApp";
    private const string Records = $"{App}/entities/stages/Ticket/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_record_api_refuses_a_change_of_a_frozen_field_after_the_freezing_state_and_keeps_the_record()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client);
        version = await PatchAsync(client, id, version, """{ "status": "submitted" }""");

        using var response = await PatchResponseAsync(client, id, version, """{ "amount": 10 }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/amount", error.Name);
        Assert.Equal(["stages.ticket.amountFrozen"], error.Value.EnumerateArray().Select(message => message.GetString()));
        Assert.Equal((5L, (long?)null, version), await fixture.TicketAsync(id));
    }

    [Fact]
    public async Task The_same_change_before_the_freezing_state_is_written()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client);

        var next = await PatchAsync(client, id, version, """{ "amount": 10 }""");

        Assert.Equal((10L, (long?)null, next), await fixture.TicketAsync(id));
    }

    [Fact]
    public async Task A_write_that_breaks_both_validations_gets_only_the_entity_validation_message()
    {
        using var client = fixture.CreateClient().SignedInAs("requester");
        var (id, version) = await CreateAsync(client);
        version = await PatchAsync(client, id, version, """{ "status": "submitted" }""");

        using var response = await PatchResponseAsync(client, id, version, """{ "amount": -1 }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/amount", error.Name);
        Assert.Equal(["stages.ticket.amountNotNegative"], error.Value.EnumerateArray().Select(message => message.GetString()));
        Assert.Equal((5L, (long?)null, version), await fixture.TicketAsync(id));
    }

    [Fact]
    public async Task A_second_step_that_reuses_the_decision_task_fails_the_instance_and_keeps_the_first_steps_counter()
    {
        var ticket = await fixture.InsertTicketAsync($"Advance {Guid.NewGuid():N}");
        Guid instance;
        using (var client = fixture.CreateClient().SignedInAs("requester"))
        using (var response = await SendAsync(client, HttpMethod.Post, $"{App}/processes/AdvanceTwice/instances", $$"""{ "subjectId": "{{ticket:D}}" }"""))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            instance = body.RootElement.GetProperty("id").GetGuid();
        }

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", (await fixture.Steps.InstanceAsync(instance)).State);

        await fixture.Steps.CompleteTaskAsync(instance, "go", "first", "requester", CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("failed", (await fixture.Steps.InstanceAsync(instance)).State);
        Assert.Equal(1L, (await fixture.TicketAsync(ticket)).Step);
    }

    private static async Task<(Guid Id, long Version)> CreateAsync(HttpClient client)
    {
        using var response = await SendAsync(
            client, HttpMethod.Post, Records, $$"""{ "values": { "title": "Ticket {{Guid.NewGuid():N}}", "amount": 5, "status": "draft" } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    // Writes the values and returns the record's new version.
    private static async Task<long> PatchAsync(HttpClient client, Guid id, long version, string values)
    {
        using var response = await PatchResponseAsync(client, id, version, values);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("version").GetInt64();
    }

    private static Task<HttpResponseMessage> PatchResponseAsync(HttpClient client, Guid id, long version, string values) =>
        SendAsync(client, HttpMethod.Patch, $"{Records}/{id}", $$"""{ "version": {{version}}, "values": {{values}} }""");

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body) =>
        client.SendAsync(Request(method, path, ModuleCoreValidationFixture.Host, body), CancellationToken);
}
