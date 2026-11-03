using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// Saving a module resource through the authoring API writes the customization layer as the difference from the
/// pinned module version, and deletes the layer when nothing differs. The platform database holds the folder's
/// write lock, so these tests run on real PostgreSQL.
/// </summary>
public sealed class StudioModuleLayerTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Url = "/api/studio/apps/PurchaseRequests/modules/department-budgets/resources";

    private const string ProcessId = "94f1cc25-6a68-435f-9383-36fc43aae860";

    private const string ScenarioId = "771fda08-a104-48da-bb08-65d1a00e26f8";

    private const string ContractId = "c413e87c-c2c2-43cb-8a39-84c3fc16cdbf";

    private const string LayerFile = "customizations/department-budgets/BudgetApproval.json";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_a_step_and_changing_an_outcome_writes_a_layer_with_only_those_two_changes()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var edited = await ReadModuleAsync(client, ProcessId);
        var steps = (JsonArray)edited["steps"]!;
        steps[0]!["outcomes"]![0]!["next"] = "financeReview";
        var step = JsonNode.Parse("""
            {
              "name": "financeReview",
              "type": "task",
              "assignee": { "role": "approver" },
              "form": "BudgetReview",
              "outcomes": [
                { "name": "approve", "next": "markApproved" },
                { "name": "reject", "next": "markRejected" }
              ]
            }
            """)!;
        steps.Add(step.DeepClone());

        using var response = await PutAsync(client, ProcessId, edited, ("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var path = Path.Combine(folder.Path, LayerFile);
        var version = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal($"\"{version}\"", response.Headers.ETag?.Tag);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Equal(LayerFile, (string?)body["file"]);
        Assert.Equal(version, (string?)body["version"]);
        Assert.NotNull(body["diagnostics"]);
        var layer = JsonNode.Parse(File.ReadAllBytes(path))!;
        Assert.Equal("layer", (string?)layer["kind"]);
        Assert.Equal("department-budgets/BudgetApproval", (string?)layer["target"]);
        var expected = JsonNode.Parse("""
            {
              "steps": [
                { "name": "review", "outcomes": [ { "name": "approve", "next": "financeReview" } ] }
              ]
            }
            """)!;
        ((JsonArray)expected["steps"]!).Add(step.DeepClone());
        Assert.True(JsonNode.DeepEquals(expected, layer["changes"]), layer["changes"]!.ToJsonString());
    }

    [Fact]
    public async Task Saving_the_module_content_over_a_layer_deletes_the_layer()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        // Read the module's own content first: once a layer exists, the GET answers the content with the layer applied.
        var module = await ReadModuleAsync(client, ProcessId);
        var version = await CreateLayerAsync(client, folder.Path);

        using var response = await PutAsync(client, ProcessId, module, ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Null(body["file"]);
        Assert.Null(body["version"]);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(File.Exists(Path.Combine(folder.Path, LayerFile)));
    }

    [Fact]
    public async Task Saving_the_module_content_when_no_layer_exists_writes_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var before = Snapshot(folder.Path);

        using var response = await PutAsync(
            client, ProcessId, await ReadModuleAsync(client, ProcessId), ("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task A_stale_precondition_answers_409_and_leaves_the_layer_unchanged()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var edited = await ReadModuleAsync(client, ProcessId);
        await CreateLayerAsync(client, folder.Path);
        var before = Snapshot(folder.Path);
        edited["entity"] = "Budget";
        edited["start"] = "markApproved";

        using var staleVersion = await PutAsync(client, ProcessId, edited, ("If-Match", $"\"{new string('0', 64)}\""));
        using var layerExists = await PutAsync(client, ProcessId, edited, ("If-None-Match", "*"));
        using var otherTag = await PutAsync(client, ProcessId, edited, ("If-None-Match", "\"abc\""));

        Assert.Equal(HttpStatusCode.Conflict, staleVersion.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, layerExists.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherTag.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task A_missing_or_doubled_precondition_is_refused_and_writes_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var before = Snapshot(folder.Path);
        var edited = await ReadModuleAsync(client, ProcessId);
        edited["start"] = "markApproved";

        using var missing = await PutAsync(client, ProcessId, edited);
        using var both = await PutAsync(client, ProcessId, edited, ("If-Match", "\"x\""), ("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task A_scenario_a_changed_name_and_a_changed_contract_transition_answer_400_and_write_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var before = Snapshot(folder.Path);
        var scenario = await ReadModuleAsync(client, ScenarioId);
        scenario["description"] = "Changed";
        var renamed = await ReadModuleAsync(client, ProcessId);
        renamed["name"] = "Renamed";
        var contract = await ReadModuleAsync(client, ContractId);
        contract["lifecycle"]!["transitions"]![0]!["guard"] = "true";

        using var scenarioResponse = await PutAsync(client, ScenarioId, scenario, ("If-None-Match", "*"));
        using var renamedResponse = await PutAsync(client, ProcessId, renamed, ("If-None-Match", "*"));
        using var contractResponse = await PutAsync(client, ContractId, contract, ("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.BadRequest, scenarioResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, renamedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, contractResponse.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "customizations")));
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task A_session_without_the_author_role_gets_403_and_no_session_gets_401_and_no_file_changes()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        var edited = await ReadModuleAsync(author, ProcessId);
        edited["start"] = "markApproved";
        var before = Snapshot(folder.Path);

        using var forbidden = await PutAsync(requester, ProcessId, edited, ("If-None-Match", "*"));
        using var unauthorized = await PutAsync(anonymous, ProcessId, edited, ("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    // Saves the process with a changed outcome target and returns the new layer's version.
    private static async Task<string> CreateLayerAsync(HttpClient client, string folder)
    {
        var edited = await ReadModuleAsync(client, ProcessId);
        edited["steps"]![0]!["outcomes"]![0]!["next"] = "markRejected";
        using var response = await PutAsync(client, ProcessId, edited, ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(folder, LayerFile)));
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, LayerFile))));
    }

    private static async Task<JsonNode> ReadModuleAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync(new Uri($"{Url}/{id}", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!["content"]!;
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, string id, JsonNode body, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"{Url}/{id}", UriKind.Relative))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static List<string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(folder, path) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))
            .ToList();

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(string folder)
    {
        var platform = await database.CreateDatabaseAsync("studio_module_layers", CancellationToken);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
    }
}
