using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The kind list route answers one page of a kind's resources with their titles in the UI locale, as
/// <c>docs/reference/studio-resource-form.md#kind-list</c> says.
/// </summary>
public sealed class StudioKindListTests : IAsyncLifetime
{
    private const string Host = "a.example.test";

    private const string RoleListUrl = "/api/studio/apps/PurchaseRequests/kinds/role/resources";

    private const string RequestEntityId = "4d2a6c81-3e5b-4f70-8a1c-9b0d2e4f6a01";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_page_the_sort_the_search_the_filter_and_the_locale_select_the_titled_items_and_the_total()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_kind_list", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");

        // A role that the live release does not hold is new, and the others are published.
        using (var create = RecordApiFixture.Request(
            HttpMethod.Post,
            "/api/studio/apps/PurchaseRequests/resources",
            Host,
            $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "auditor", "formatVersion": 1 }"""))
        using (var response = await author.SendAsync(create, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var all = await GetAsync(author, $"{RoleListUrl}?pageSize=100");
        var titles = Titles(all);
        var total = all.GetProperty("totalCount").GetInt64();
        Assert.True(total >= 6);
        Assert.Equal(total, titles.Count);
        Assert.Equal([.. titles.Order(StringComparer.OrdinalIgnoreCase)], titles);
        Assert.Contains("Department manager", titles);
        Assert.Contains("auditor", titles);
        Assert.Equal(100, all.GetProperty("pageSize").GetInt32());

        var second = await GetAsync(author, $"{RoleListUrl}?pageSize=2&page=2");
        Assert.Equal(titles.Skip(2).Take(2), Titles(second));
        Assert.Equal(total, second.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(2, second.GetProperty("pageSize").GetInt32());

        var past = await GetAsync(author, $"{RoleListUrl}?pageSize=100&page=2");
        Assert.Empty(Titles(past));
        Assert.Equal(total, past.GetProperty("totalCount").GetInt64());

        Assert.Equal([.. titles.AsEnumerable().Reverse()], Titles(await GetAsync(author, $"{RoleListUrl}?pageSize=100&sort=-name")));

        var searched = await GetAsync(author, $"{RoleListUrl}?pageSize=100&search=finance");
        Assert.Equal(["Finance reviewer"], Titles(searched));
        Assert.Equal(1, searched.GetProperty("totalCount").GetInt64());

        var statusFilter = Uri.EscapeDataString("""{"all":[{"field":"status","op":"eq","value":"published"}]}""");
        var published = await GetAsync(author, $"{RoleListUrl}?pageSize=100&filter={statusFilter}");
        Assert.Equal(total - 1, published.GetProperty("totalCount").GetInt64());
        Assert.DoesNotContain("auditor", Titles(published));
        Assert.All(published.GetProperty("items").EnumerateArray(), item => Assert.Equal("published", item.GetProperty("status").GetString()));

        var byStatus = await GetAsync(author, $"{RoleListUrl}?pageSize=100&sort=status");
        Assert.Equal("auditor", Titles(byStatus)[0]);
        Assert.Equal("new", byStatus.GetProperty("items")[0].GetProperty("status").GetString());
        var byStatusDescending = await GetAsync(author, $"{RoleListUrl}?pageSize=100&sort=-status");
        Assert.Equal("auditor", Titles(byStatusDescending)[^1]);

        var vietnamese = Titles(await GetAsync(author, $"{RoleListUrl}?pageSize=100&locale=vi"));
        Assert.Contains("Trưởng phòng", vietnamese);
        Assert.DoesNotContain("Department manager", vietnamese);

        var languages = await GetAsync(author, "/api/studio/apps/PurchaseRequests/kinds/text/resources?pageSize=100");
        var english = languages.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("locale").GetString() == "en" && item.GetProperty("module").ValueKind == JsonValueKind.Null);
        Assert.Equal("en", english.GetProperty("title").GetString());

        var none = await GetAsync(author, "/api/studio/apps/PurchaseRequests/kinds/nothing/resources");
        Assert.Empty(Titles(none));
        Assert.Equal(0, none.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_filter_or_a_sort_on_a_column_the_list_does_not_hold_answers_400()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_kind_list_400", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");

        var undeclared = Uri.EscapeDataString("""{"all":[{"field":"file","op":"empty"}]}""");
        await AssertRefusedAsync(author, $"{RoleListUrl}?filter={undeclared}", "filter");
        await AssertRefusedAsync(author, $"{RoleListUrl}?sort=file", "sort");
        await AssertRefusedAsync(author, $"{RoleListUrl}?pageSize=101", "pageSize");
    }

    [Fact]
    public async Task The_entity_list_of_an_application_that_pins_a_module_holds_its_own_entities_and_the_modules_each_with_its_module()
    {
        var modules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp"), folder.Path);
        Directory.CreateDirectory(Path.Combine(folder.Path, "entities"));
        await File.WriteAllTextAsync(
            Path.Combine(folder.Path, "entities", "ticket.json"),
            $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "entity", "name": "Ticket", "formatVersion": 1, "label": { "textKey": "ticket.label" }, "displayField": "title", "fields": [{ "name": "title", "type": "text", "required": true, "maxLength": 100, "label": { "textKey": "ticket.title" } }] }""",
            CancellationToken);
        var texts = Path.Combine(folder.Path, "texts", "en.json");
        await File.WriteAllTextAsync(
            texts,
            (await File.ReadAllTextAsync(texts, CancellationToken)).Replace(
                "\"role.employee\": \"Employee\"", "\"role.employee\": \"Employee\", \"ticket.label\": \"Ticket\", \"ticket.title\": \"Title\"", StringComparison.Ordinal),
            CancellationToken);
        var connectionString = await _database.CreateDatabaseAsync("studio_kind_list_modules", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path, modules);
        using var author = factory.CreateClient().SignedInAs("author");

        var list = await GetAsync(author, "/api/studio/apps/ModuleContractApp/kinds/entity/resources?pageSize=100");

        var items = list.GetProperty("items").EnumerateArray().ToList();
        var own = items.Single(item => item.GetProperty("module").ValueKind == JsonValueKind.Null);
        Assert.Equal("Ticket", own.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, own.GetProperty("moduleLabel").ValueKind);
        var shared = items.Single(item => item.GetProperty("id").GetString() == RequestEntityId);
        Assert.Equal("requests", shared.GetProperty("module").GetString());
        Assert.Equal("Requests", shared.GetProperty("moduleLabel").GetString());
        Assert.Equal(items.Count, list.GetProperty("totalCount").GetInt64());

        // The search covers the module's label, so it finds the module's resources.
        var searched = await GetAsync(author, "/api/studio/apps/ModuleContractApp/kinds/entity/resources?search=requests");
        Assert.DoesNotContain(searched.GetProperty("items").EnumerateArray(), item => item.GetProperty("module").ValueKind == JsonValueKind.Null);
        Assert.Contains(searched.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == RequestEntityId);
    }

    [Fact]
    public async Task A_user_without_the_author_role_gets_403_and_a_request_without_a_session_gets_401()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_kind_list_denied", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(RoleListUrl, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(RoleListUrl, Host), CancellationToken)).StatusCode);
    }

    private static List<string> Titles(JsonElement list) =>
        [.. list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()!)];

    private static async Task AssertRefusedAsync(HttpClient client, string url, string parameter)
    {
        using var request = RecordApiFixture.Request(url, Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Contains(parameter, body.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
    }

    private static Task<JsonElement> GetAsync(HttpClient client, string url) => StudioReleaseHistoryTests.GetAsync(client, url);
}
