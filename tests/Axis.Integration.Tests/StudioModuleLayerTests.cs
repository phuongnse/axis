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
/// pinned module version, and deletes the layer when nothing differs. Resetting one element rewrites the layer the same way. The platform database holds the folder's
/// write lock, so these tests run on real PostgreSQL.
/// </summary>
public sealed class StudioModuleLayerTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Url = "/api/studio/apps/PurchaseRequests/modules/department-budgets/resources";

    private const string ProcessId = "94f1cc25-6a68-435f-9383-36fc43aae860";

    private const string TextId = "b90b32ef-6b04-49d1-a001-88457d552c76";

    private const string TextLayerFile = "customizations/department-budgets/TextsEn.json";

    private const string LabelKey = "department-budgets.budget.label";

    private const string NameKey = "department-budgets.budget.name";

    private const string ScenarioId = "771fda08-a104-48da-bb08-65d1a00e26f8";

    private const string ContractId = "c413e87c-c2c2-43cb-8a39-84c3fc16cdbf";

    private const string FormId = "12dffe7c-7ed8-47c0-9ed5-fa9b4251d85e";

    private const string FormLayerFile = "customizations/department-budgets/BudgetReview.json";

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

    [Fact]
    public async Task Resetting_the_added_step_of_a_two_change_layer_leaves_only_the_outcome_change()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var edited = await ReadModuleAsync(client, ProcessId);
        var steps = (JsonArray)edited["steps"]!;
        steps[0]!["outcomes"]![0]!["next"] = "financeReview";
        steps.Add(JsonNode.Parse("""{ "name": "financeReview", "type": "end" }""")!);
        using var saved = await PutAsync(client, ProcessId, edited, ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var path = Path.Combine(folder.Path, LayerFile);
        var version = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        using var response = await ResetAsync(client, ProcessId, "/steps", "financeReview", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var layerBytes = File.ReadAllBytes(path);
        var newVersion = Convert.ToHexStringLower(SHA256.HashData(layerBytes));
        Assert.NotEqual(version, newVersion);
        Assert.Equal($"\"{newVersion}\"", response.Headers.ETag?.Tag);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Equal(LayerFile, (string?)body["file"]);
        Assert.Equal(newVersion, (string?)body["version"]);
        Assert.NotNull(body["diagnostics"]);
        var expected = JsonNode.Parse("""
            { "steps": [ { "name": "review", "outcomes": [ { "name": "approve", "next": "financeReview" } ] } ] }
            """)!;
        var layer = JsonNode.Parse(layerBytes)!;
        Assert.True(JsonNode.DeepEquals(expected, layer["changes"]), layer["changes"]!.ToJsonString());
    }

    [Fact]
    public async Task Resetting_a_removed_outcome_brings_the_modules_outcome_back_and_the_layer_loses_its_remove()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        const string layer = """
            { "id": "6f1d9c2a-3b4e-4d58-9a7c-1e2f3a4b5c6d", "kind": "layer", "formatVersion": 1, "target": "department-budgets/BudgetApproval",
              "changes": { "steps": [
                { "name": "review", "dueIn": "P3D", "outcomes": [{ "name": "reject", "remove": true }] },
                { "name": "archive", "after": "markRejected", "type": "end" } ] } }
            """;
        var path = Path.Combine(folder.Path, LayerFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, layer, CancellationToken);
        var version = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        using var response = await ResetAsync(client, ProcessId, "/steps/0/outcomes", "reject", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var read = await client.GetAsync(new Uri($"{Url}/{ProcessId}", UriKind.Relative), CancellationToken);
        var answer = JsonNode.Parse(await read.Content.ReadAsStringAsync(CancellationToken))!;
        var outcomes = (JsonArray)answer["content"]!["steps"]![0]!["outcomes"]!;
        Assert.Equal(["approve", "reject"], outcomes.Select(outcome => (string?)outcome!["name"]));
        Assert.Equal("markRejected", (string?)outcomes[1]!["next"]);
        Assert.Empty((JsonArray)answer["removed"]!);
        var text = await File.ReadAllTextAsync(path, CancellationToken);
        Assert.DoesNotContain("\"remove\"", text, StringComparison.Ordinal);
        Assert.Contains("\"dueIn\"", text, StringComparison.Ordinal);
        Assert.Contains("\"archive\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resetting_the_only_change_of_a_layer_deletes_the_layer_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var version = await CreateLayerAsync(client, folder.Path);

        using var response = await ResetAsync(client, ProcessId, "/steps/0/outcomes", "approve", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Null(body["file"]);
        Assert.Null(body["version"]);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(File.Exists(Path.Combine(folder.Path, LayerFile)));
    }

    [Fact]
    public async Task Resetting_a_moved_section_field_deletes_the_layer_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        const string layer = """
            { "id": "b3a7c1d2-5e4f-4a68-8c9d-2f1e0d3c4b5a", "kind": "layer", "formatVersion": 1, "target": "department-budgets/BudgetReview",
              "changes": { "sections": [ { "name": "main", "fields": [ { "field": "amount", "after": "name" } ] } ] } }
            """;
        var path = Path.Combine(folder.Path, FormLayerFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, layer, CancellationToken);
        var version = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        using var response = await ResetAsync(client, FormId, "/sections/0/fields", "amount", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Null(body["file"]);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_stale_or_missing_version_on_a_reset_answers_409_or_428_and_writes_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        await CreateLayerAsync(client, folder.Path);
        var before = Snapshot(folder.Path);

        using var stale = await ResetAsync(client, ProcessId, "/steps/0/outcomes", "approve", ("If-Match", $"\"{new string('0', 64)}\""));
        using var missing = await ResetAsync(client, ProcessId, "/steps/0/outcomes", "approve");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task A_reset_with_no_layer_answers_409_and_a_key_that_names_no_element_answers_404()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var before = Snapshot(folder.Path);

        using var noLayer = await ResetAsync(client, ProcessId, "/steps/0/outcomes", "approve", ("If-Match", $"\"{new string('0', 64)}\""));
        var version = await CreateLayerAsync(client, folder.Path);
        var withLayer = Snapshot(folder.Path);
        using var unknownKey = await ResetAsync(client, ProcessId, "/steps/0/outcomes", "missing", ("If-Match", $"\"{version}\""));
        using var unknownList = await ResetAsync(client, ProcessId, "/nothing", "approve", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.Conflict, noLayer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownKey.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownList.StatusCode);
        Assert.NotEqual(before, withLayer);
        Assert.Equal(withLayer, Snapshot(folder.Path));
    }

    [Fact]
    public async Task Deleting_the_layer_resource_resets_the_whole_resource()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        await CreateLayerAsync(client, folder.Path);
        using var before = await client.GetAsync(new Uri($"{Url}/{ProcessId}", UriKind.Relative), CancellationToken);
        var layer = JsonNode.Parse(await before.Content.ReadAsStringAsync(CancellationToken))!["layer"]!;
        using var delete = new HttpRequestMessage(
            HttpMethod.Delete, new Uri($"/api/studio/apps/PurchaseRequests/resources/{(string?)layer["id"]}", UriKind.Relative));
        delete.Headers.Add("X-Axis-Csrf", "1");
        delete.Headers.TryAddWithoutValidation("If-Match", $"\"{(string?)layer["version"]}\"");

        using var deleted = await client.SendAsync(delete, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.False(File.Exists(Path.Combine(folder.Path, LayerFile)));
        using var after = await client.GetAsync(new Uri($"{Url}/{ProcessId}", UriKind.Relative), CancellationToken);
        var answer = JsonNode.Parse(await after.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Null(answer["layer"]);
        Assert.Empty((JsonArray)answer["marks"]!);
        Assert.Empty((JsonArray)answer["removed"]!);
    }

    [Fact]
    public async Task A_reset_from_a_session_without_the_author_role_gets_403_and_without_a_session_401_and_no_file_changes()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        var version = await CreateLayerAsync(author, folder.Path);
        var before = Snapshot(folder.Path);

        using var forbidden = await ResetAsync(requester, ProcessId, "/steps/0/outcomes", "approve", ("If-Match", $"\"{version}\""));
        using var unauthorized = await ResetAsync(anonymous, ProcessId, "/steps/0/outcomes", "approve", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task Resetting_one_changed_text_key_brings_the_modules_text_back_and_keeps_the_other_change()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var version = await CreateTextLayerAsync(client, folder.Path);

        using var response = await ResetAsync(client, TextId, "/texts", LabelKey, ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var layer = JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder.Path, TextLayerFile)))!;
        var expected = JsonNode.Parse($$"""{ "texts": { "{{NameKey}}": "Title" } }""")!;
        Assert.True(JsonNode.DeepEquals(expected, layer["changes"]), layer["changes"]!.ToJsonString());
        using var read = await client.GetAsync(new Uri($"{Url}/{TextId}", UriKind.Relative), CancellationToken);
        var answer = JsonNode.Parse(await read.Content.ReadAsStringAsync(CancellationToken))!;
        Assert.Equal("Budget", (string?)answer["content"]!["texts"]![LabelKey]);
        var marks = ((JsonArray)answer["marks"]!).Select(mark => (string?)mark!["path"]).ToList();
        Assert.Equal([$"/texts/{NameKey}"], marks);
    }

    [Fact]
    public async Task Resetting_a_text_key_that_neither_the_layer_nor_the_module_has_answers_404()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var version = await CreateTextLayerAsync(client, folder.Path);
        var before = Snapshot(folder.Path);

        using var response = await ResetAsync(client, TextId, "/texts", "department-budgets.missing", ("If-Match", $"\"{version}\""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    // Saves the module text with two changed keys and returns the new layer's version.
    private static async Task<string> CreateTextLayerAsync(HttpClient client, string folder)
    {
        var edited = await ReadModuleAsync(client, TextId);
        edited["texts"]![LabelKey] = "Budget line";
        edited["texts"]![NameKey] = "Title";
        using var response = await PutAsync(client, TextId, edited, ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, TextLayerFile))));
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

    private static async Task<HttpResponseMessage> ResetAsync(
        HttpClient client, string id, string list, string key, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{Url}/{id}/reset", UriKind.Relative))
        {
            Content = new StringContent(new JsonObject { ["list"] = list, ["key"] = key }.ToJsonString(), Encoding.UTF8, "application/json"),
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
