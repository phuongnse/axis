using System.Net;
using System.Text.Json;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A read-only tenant refuses every request that writes with a <c>403</c> whose <c>reason</c> is <c>tenant-read-only</c>
/// before the route runs, and keeps reads and the open list working, see <c>docs/reference/subscriptions.md#the-read-only-tenant</c>.
/// The host marks tenant A read-only through a catalog that wraps the fixture's.
/// </summary>
public sealed class ReadOnlyTenantTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Title = "This workspace is read-only.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_write_route_in_the_endpoint_map_answers_the_read_only_403_unless_it_is_on_the_open_list()
    {
        await using var factory = ReadOnlyFactory();
        using var requester = factory.CreateClient().SignedInAs("requester");
        var failures = new List<string>();
        var checkedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var route in ApiRoutes.From(factory.Services))
        {
            if (!IsWrite(route.Method)
                || route.Pattern.RawText!.StartsWith("/api/cell", StringComparison.OrdinalIgnoreCase)
                || ReadOnlyTenant.OpenWrites.Contains(route.Name))
            {
                continue;
            }

            checkedKeys.Add(route.Name);
            using var request = Request(new HttpMethod(route.Method), route.Path, HostA, "{}");
            using var response = await requester.SendAsync(request, CancellationToken);
            var problem = await ProblemAsync(response);
            if (response.StatusCode != HttpStatusCode.Forbidden || !IsReadOnlyRefusal(problem))
            {
                failures.Add($"{route.Name} answered {(int)response.StatusCode}");
            }
        }

        Assert.NotEmpty(checkedKeys);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task The_open_list_routes_that_exist_do_not_answer_the_read_only_403()
    {
        await using var factory = ReadOnlyFactory();
        var open = ApiRoutes.From(factory.Services).Where(route => ReadOnlyTenant.OpenWrites.Contains(route.Name)).ToList();

        // Sign-out, the two session routes and the tenant export start exist.
        Assert.Contains(open, route => route.Name == "POST /api/auth/sign-out");
        Assert.Contains(open, route => route.Name == "DELETE /api/me/sessions");
        Assert.Contains(open, route => route.Name == "DELETE /api/me/sessions/{id}");
        Assert.Contains(open, route => route.Name == "POST /api/tenant/exports");
        foreach (var route in open)
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            using var request = Request(new HttpMethod(route.Method), route.Path, HostA, "{}");
            using var response = await client.SendAsync(request, CancellationToken);

            Assert.False(IsReadOnlyRefusal(await ProblemAsync(response)), $"{route.Name} answered the read-only refusal.");
        }
    }

    [Fact]
    public async Task Reads_lists_history_data_sources_and_exports_still_answer_in_a_read_only_tenant()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Alpha", ["quantity"] = "3", ["status"] = "open" });
        await using var factory = ReadOnlyFactory();
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var exporter = factory.CreateClient().SignedInAs("eve");

        foreach (var (client, path) in new[]
        {
            (requester, Items),
            (requester, $"{Items}/{id}"),
            (requester, $"{Items}/{id}/history"),
            (requester, "/api/apps/RecordsApp/data-sources/ItemsByName/rows"),
            (exporter, $"{Items}/export?format=csv"),
            (exporter, "/api/apps/RecordsApp/data-sources/ItemCountsByDepartment/export?format=csv"),
        })
        {
            using var response = await client.SendAsync(Request(path, HostA), CancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public async Task An_invalid_body_gets_the_read_only_403_and_writes_no_row_while_an_active_tenant_answers_400()
    {
        await fixture.ResetAsync();
        var (_, before) = await fixture.TableShapeAsync(TenantA, "Item");
        await using var factory = ReadOnlyFactory();
        using var requester = factory.CreateClient().SignedInAs("requester");

        using var refused = await requester.SendAsync(Request(HttpMethod.Post, Items, HostA, "{not json"), CancellationToken);
        using var active = await fixture.Client.SendAsync(Request(HttpMethod.Post, Items, HostA, "{not json"), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.True(IsReadOnlyRefusal(await ProblemAsync(refused)));
        Assert.Equal(HttpStatusCode.BadRequest, active.StatusCode);
        Assert.Equal(before, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
    }

    private WebApplicationFactory<Program> ReadOnlyFactory() =>
        fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(provider => new TenantResolver(new ReadOnlyCatalog(provider.GetRequiredService<ITenantCatalog>())))));

    private static bool IsWrite(string method) =>
        method is "POST" or "PUT" or "PATCH" or "DELETE";

    private static bool IsReadOnlyRefusal(JsonDocument? problem) =>
        problem is not null
        && problem.RootElement.ValueKind == JsonValueKind.Object
        && problem.RootElement.TryGetProperty("reason", out var reason)
        && reason.GetString() == "tenant-read-only"
        && problem.RootElement.TryGetProperty("title", out var title)
        && title.GetString() == Title;

    // The body of a problem answer, or null when the answer holds no JSON.
    private static async Task<JsonDocument?> ProblemAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(CancellationToken);
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ReadOnlyCatalog(ITenantCatalog inner) : ITenantCatalog
    {
        public IReadOnlyList<TenantEntry> Tenants => inner.Tenants;

        public TenantEntry? Find(string tenantId) => inner.Find(tenantId) is { } entry ? entry with { ReadOnly = true } : null;

        public TenantEntry? FindByHost(string host) => inner.FindByHost(host) is { } entry ? entry with { ReadOnly = true } : null;

        public bool IsUnavailableHost(string host) => inner.IsUnavailableHost(host);
    }
}
