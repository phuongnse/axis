using System.Net;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The storage check of the write routes, see <c>docs/reference/tenant-quotas.md#refusals</c>. The host is a cell whose
/// storage quota is 1000 bytes, see <see cref="StorageQuotaHost"/>. Tenant <c>a</c> has a latest sample of 2000 bytes
/// and <c>b</c> one of 10. The 2000 are the database size plus the file bytes. The platform tables live in tenant <c>a</c>'s database here, where the server reads them.
/// </summary>
public sealed class StorageQuotaTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string ItemImports = "/api/apps/RecordsApp/entities/Item/imports";
    private const string Receipt = "/api/apps/FilesApp/entities/Claim/fields/receipt/files";
    private const string Title = "This tenant has reached its storage quota.";
    private const long Limit = 1000;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Over_the_quota_a_record_create_and_update_an_import_upload_and_commit_and_a_file_upload_are_refused_and_write_nothing()
    {
        await fixture.ResetAsync();
        var item = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        var cases = new (string Name, Func<HttpRequestMessage> Request)[]
        {
            ("create", () => Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Chair" } }""")),
            ("update", () => Request(HttpMethod.Patch, $"{Items}/{item:D}", HostA, """{ "version": 1, "values": { "name": "Chair" } }""")),
            ("import upload", () => Request(HttpMethod.Post, ItemImports, HostA, "name\nChair\n", "text/csv")),
            ("import commit", () => Request(HttpMethod.Post, $"{ItemImports}/{Guid.NewGuid():D}/commit", HostA)),
            ("file upload", Upload),
        };
        await using var host = CreateCellHost();
        using var client = host.CreateClient().SignedInAs("requester");
        await SeedAsync(TenantA, 1500, 500);
        try
        {
            var rows = (await fixture.TableShapeAsync(TenantA, "Item")).RowCount;
            var audits = await fixture.AuditRecordCountAsync(TenantA);
            var imports = await CountAsync("axis.record_imports");
            var files = await CountAsync("axis.files");
            var contents = await CountAsync("axis.file_contents");
            var logged = Refusals();

            foreach (var (name, request) in cases)
            {
                using var message = request();
                using var response = await client.SendAsync(message, CancellationToken);

                using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
                var root = problem.RootElement;
                Assert.True(Title == root.GetProperty("title").GetString(), name);
                Assert.Equal("quota-exceeded", root.GetProperty("reason").GetString());
                Assert.Equal("storage", root.GetProperty("quota").GetString());
                Assert.Equal(Limit, root.GetProperty("limit").GetInt64());
                Assert.Equal(2000, root.GetProperty("used").GetInt64());
            }

            Assert.Equal(rows, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
            Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
            Assert.Equal(imports, await CountAsync("axis.record_imports"));
            Assert.Equal(files, await CountAsync("axis.files"));
            Assert.Equal(contents, await CountAsync("axis.file_contents"));
            Assert.Equal(logged + cases.Length, Refusals());
        }
        finally
        {
            await ClearAsync();
        }
    }

    [Fact]
    public async Task Over_the_quota_a_delete_still_answers_204_and_a_malformed_body_gets_the_quota_403_not_a_400()
    {
        await fixture.ResetAsync();
        var item = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        await using var host = CreateCellHost();
        using var client = host.CreateClient().SignedInAs("requester");
        await SeedAsync(TenantA, 2000, 0);
        try
        {
            using var malformed = Request(HttpMethod.Post, Items, HostA, "{");
            using var refused = await client.SendAsync(malformed, CancellationToken);
            using var delete = Request(HttpMethod.Delete, $"{Items}/{item:D}", HostA);
            using var deleted = await client.SendAsync(delete, CancellationToken);

            using var problem = await ReadProblemAsync(refused, HttpStatusCode.Forbidden);
            Assert.Equal("quota-exceeded", problem.RootElement.GetProperty("reason").GetString());
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Equal(0L, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        }
        finally
        {
            await ClearAsync();
        }
    }

    [Fact]
    public async Task A_tenant_under_its_quota_and_a_tenant_with_no_sample_are_not_refused()
    {
        await fixture.ResetAsync();
        await using var host = CreateCellHost();
        using var client = host.CreateClient().SignedInAs("requester");
        await SeedAsync(TenantB, 10, 0);
        try
        {
            using var underQuota = Request(HttpMethod.Post, Items, HostB, """{ "values": { "name": "Desk" } }""");
            using var underResponse = await client.SendAsync(underQuota, CancellationToken);
            using var noSample = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }""");
            using var noSampleResponse = await client.SendAsync(noSample, CancellationToken);

            Assert.Equal(HttpStatusCode.Created, underResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Created, noSampleResponse.StatusCode);
        }
        finally
        {
            await ClearAsync();
        }
    }

    [Fact]
    public async Task A_server_outside_a_cell_never_refuses_even_when_the_sample_is_over_the_configured_default()
    {
        await fixture.ResetAsync();
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Quotas:Defaults:storage", "1"));
        using var client = host.CreateClient().SignedInAs("requester");
        await SeedAsync(TenantA, 2000, 0);
        try
        {
            using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }""");
            using var response = await client.SendAsync(request, CancellationToken);
            using var plain = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Chair" } }""");
            using var plainResponse = await fixture.Client.SendAsync(plain, CancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(HttpStatusCode.Created, plainResponse.StatusCode);
        }
        finally
        {
            await ClearAsync();
        }
    }

    private static HttpRequestMessage Upload()
    {
        var request = Request(HttpMethod.Post, Receipt, HostA);
        var bytes = new byte[100];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        var part = new ByteArrayContent(bytes);
        part.Headers.TryAddWithoutValidation("Content-Type", "application/pdf");
        request.Content = new MultipartFormDataContent { { part, "file", "invoice.pdf" } };
        return request;
    }

    private WebApplicationFactory<Program> CreateCellHost() => fixture.Factory.WithWebHostBuilder(builder => builder.InCell(Limit));

    private int Refusals() =>
        fixture.Logs.Entries.Count(entry => entry.Contains("quota.refused: tenant a reached its storage quota.", StringComparison.Ordinal));

    private async Task<long> CountAsync(string table) => (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {table}"))!;

    private Task SeedAsync(string tenant, long databaseBytes, long fileBytes) =>
        StorageQuotaHost.SeedUsageAsync(fixture.ConnectionString(TenantA), tenant, databaseBytes, fileBytes, CancellationToken);

    private Task ClearAsync() => StorageQuotaHost.ClearUsageAsync(fixture.ConnectionString(TenantA), CancellationToken);
}
