using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Default-deny on the record API and the data source endpoint. A request without a session is a
/// 401 before the path is read, and a signed-in user needs a policy grant for the entity or data
/// source and the action, or the answer is a 403 before the query or body is read. Neither reads
/// or writes any data. The RecordsApp fixture gives each test user a different set of grants.
/// </summary>
public sealed class RecordPolicyTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string ActiveItems = "/api/apps/RecordsApp/data-sources/ActiveItems/rows";
    private const string SignInTitle = "Sign in to read or write this data.";
    private const string ForbiddenTitle = "You have no grant for this action.";

    // A body the record API would refuse as 415 if it got that far.
    private const string PlainBody = "{";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every record route and the data source endpoint. <c>{id}</c> stands for a stored item. The
    /// query and the bodies are invalid, so a request that got past the grant would be a 400 or a
    /// 415 instead.
    /// </summary>
    private static readonly (string Method, string Path, string? Body)[] _routes =
    [
        ("GET", $"{Items}?page=0", null),
        ("GET", $"{Items}/{{id}}", null),
        ("GET", $"{Items}/{{id}}/history?pageSize=0", null),
        ("POST", Items, PlainBody),
        ("PATCH", $"{Items}/{{id}}", PlainBody),
        ("DELETE", $"{Items}/{{id}}", null),
        ("GET", $"{ActiveItems}?page=0", null),
    ];

    public static TheoryData<string, string, string?> Routes() => new(_routes);

    /// <summary><see cref="Routes"/> and the same routes under an application that does not exist.</summary>
    public static TheoryData<string, string, string?> RoutesAndUnknownApplications() =>
        new(_routes.Concat(_routes.Select(route => route with { Path = route.Path.Replace("/RecordsApp/", "/NoSuchApp/", StringComparison.Ordinal) })));

    [Theory]
    [MemberData(nameof(RoutesAndUnknownApplications))]
    public async Task Every_route_is_401_without_a_session_and_writes_nothing(string method, string path, string? body)
    {
        using var client = fixture.CreateClient();

        await AssertRefusedAsync(client, method, path, body, HttpStatusCode.Unauthorized, SignInTitle);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_route_is_403_for_a_user_without_a_grant_and_writes_nothing(string method, string path, string? body)
    {
        using var client = fixture.CreateClient().SignedInAs("binh");

        await AssertRefusedAsync(client, method, path, body, HttpStatusCode.Forbidden, ForbiddenTitle);
    }

    [Fact]
    public async Task A_read_grant_allows_list_and_read_and_refuses_create_update_and_delete()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        using var rita = fixture.CreateClient().SignedInAs("rita");

        using (var list = await SendAsync(rita, "GET", Items, null))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var page = await ReadJsonAsync(list);
            Assert.Equal(id, Assert.Single(page.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        using (var read = await SendAsync(rita, "GET", $"{Items}/{id:D}", null))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        }

        using (var create = await SendAsync(rita, "POST", Items, """{ "values": { "name": "Chair" } }"""))
        {
            await AssertProblemAsync(create, HttpStatusCode.Forbidden, ForbiddenTitle);
        }

        using (var update = await SendAsync(rita, "PATCH", $"{Items}/{id:D}", """{ "version": 1, "values": { "name": "Table" } }"""))
        {
            await AssertProblemAsync(update, HttpStatusCode.Forbidden, ForbiddenTitle);
        }

        using (var delete = await SendAsync(rita, "DELETE", $"{Items}/{id:D}", null))
        {
            await AssertProblemAsync(delete, HttpStatusCode.Forbidden, ForbiddenTitle);
        }

        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, id));
        using var stored = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}", null));
        Assert.Equal(1, stored.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("Desk", stored.RootElement.GetProperty("values").GetProperty("name").GetString());
    }

    // uma's roles claim holds "Employee" and "auditor". The application declares "employee", and
    // a claim value matches a role only exactly.
    [Fact]
    public async Task Role_claim_values_that_name_no_declared_role_grant_nothing()
    {
        using var uma = fixture.CreateClient().SignedInAs("uma");

        using var list = await SendAsync(uma, "GET", Items, null);

        await AssertProblemAsync(list, HttpStatusCode.Forbidden, ForbiddenTitle);
    }

    // dora holds a read grant on ActiveItems and none on Item, its root entity. requester holds both.
    [Fact]
    public async Task A_data_source_grant_without_a_read_grant_on_its_root_entity_is_403()
    {
        using var dora = fixture.CreateClient().SignedInAs("dora");

        using (var rows = await SendAsync(dora, "GET", ActiveItems, null))
        {
            await AssertProblemAsync(rows, HttpStatusCode.Forbidden, ForbiddenTitle);
        }

        using var allowed = await SendAsync(fixture.Client, "GET", ActiveItems, null);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    /// <summary>
    /// Sends <paramref name="method"/> to <paramref name="path"/> for a stored item, and asserts the
    /// refusal and that the item, its audit records and the item count are unchanged.
    /// </summary>
    private async Task AssertRefusedAsync(HttpClient client, string method, string path, string? body, HttpStatusCode status, string title)
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });

        using var response = await SendAsync(client, method, path.Replace("{id}", id.ToString("D"), StringComparison.Ordinal), body, "text/plain");

        await AssertProblemAsync(response, status, title);
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, id));
        using var stored = await ReadJsonAsync(await SendAsync(fixture.Client, "GET", $"{Items}/{id:D}", null));
        Assert.Equal(1, stored.RootElement.GetProperty("version").GetInt64());
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        using var problem = await ReadProblemAsync(response, status);
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string method,
        string path,
        string? body,
        string contentType = "application/json")
    {
        using var request = Request(new HttpMethod(method), path, HostA, body, contentType);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }
    }
}
