using System.Net;
using System.Text.Json;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>approvals</c> module holds a policy for its <c>approver</c> slot. ModuleAppA maps the slot to
/// <c>manager</c> and ModuleAppB to <c>reviewer</c>, so the policy grants the role each application maps,
/// and only there.
/// </summary>
public sealed class ModulePolicyTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string AppA = "/api/apps/ModuleAppA";
    private const string AppB = "/api/apps/ModuleAppB";
    private const string Approvals = "entities/approvals/Approval/records";
    private const string ApprovalsDataSource = "data-sources/approvals/Approvals/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_person_who_holds_the_mapped_role_reads_and_creates_module_records()
    {
        using var client = fixture.CreateClient().SignedInAs("approver-a");
        var title = $"Approval {Guid.NewGuid():N}";

        using (var create = RecordApiFixture.Request(
            HttpMethod.Post, $"{AppA}/{Approvals}", ModuleAppFixture.Host, $$"""{ "values": { "title": "{{title}}" } }"""))
        {
            using var created = await client.SendAsync(create, CancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (var list = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{Approvals}", ModuleAppFixture.Host))
        {
            using var response = await client.SendAsync(list, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Contains(
                body.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("values").GetProperty("title").GetString() == title);
        }

        using (var rows = RecordApiFixture.Request(HttpMethod.Get, $"{AppA}/{ApprovalsDataSource}", ModuleAppFixture.Host))
        {
            using var response = await client.SendAsync(rows, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_person_without_the_mapped_role_is_denied_the_module_entity_and_data_source()
    {
        using var client = fixture.CreateClient().SignedInAs("manager");

        foreach (var (method, path, body) in new[]
        {
            (HttpMethod.Get, $"{AppA}/{Approvals}", null),
            (HttpMethod.Post, $"{AppA}/{Approvals}", """{ "values": { "title": "Denied" } }"""),
            (HttpMethod.Get, $"{AppA}/{ApprovalsDataSource}", null),
        })
        {
            using var request = RecordApiFixture.Request(method, path, ModuleAppFixture.Host, body);
            using var response = await client.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_role_held_in_one_application_gets_nothing_where_the_slot_maps_to_another_role()
    {
        using var client = fixture.CreateClient().SignedInAs("approver-a");

        foreach (var (method, path, body) in new[]
        {
            (HttpMethod.Get, $"{AppB}/{Approvals}", null),
            (HttpMethod.Post, $"{AppB}/{Approvals}", """{ "values": { "title": "Denied" } }"""),
            (HttpMethod.Get, $"{AppB}/{ApprovalsDataSource}", null),
        })
        {
            using var request = RecordApiFixture.Request(method, path, ModuleAppFixture.Host, body);
            using var response = await client.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
        }
    }
}
