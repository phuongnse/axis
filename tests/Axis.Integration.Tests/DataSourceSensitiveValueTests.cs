using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A data source reads a projected sensitive field as its plain value. It runs on the FilesApp fixture in tenant A, where the
/// <c>Claims</c> data source projects the <c>title</c> and the sensitive <c>note</c> of a claim. The host of the fixture holds no
/// data key for tenant A, so a test derives a host that holds one. A user whose grants do not cover the field is refused.
/// </summary>
public sealed class DataSourceSensitiveValueTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";
    private const string Rows = "/api/apps/FilesApp/data-sources/Claims/rows?pageSize=100";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Keyed() =>
        fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Tenants:a:DataKeys:1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    private async Task DeleteAsync(Guid claim)
    {
        Assert.True(fixture.FilesModel.TryGetEntity("Claim", out var entity));
        await fixture.ExecuteAsync(
            TenantA,
            $"DELETE FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = '{claim:D}'");
    }

    [Fact]
    public async Task The_rows_route_returns_the_plain_value_of_a_sensitive_field_to_a_caller_whose_grants_cover_it()
    {
        await using var keyed = Keyed();
        using var requester = keyed.CreateClient().SignedInAs("requester");
        const string note = "Handwritten note of Ngô Thị Hạnh";
        using var created = await requester.SendAsync(
            Request(HttpMethod.Post, Claims, HostA, $$"""{ "values": { "title": "Data source sealed", "note": "{{note}}" } }"""), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var claim = createdBody.RootElement.GetProperty("id").GetGuid();
        try
        {
            using var response = await requester.SendAsync(Request(Rows, HostA), CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            var item = page.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == claim);
            Assert.Equal(note, item.GetProperty("values").GetProperty("note").GetString());
            Assert.Equal("Data source sealed", item.GetProperty("values").GetProperty("title").GetString());
        }
        finally
        {
            await DeleteAsync(claim);
        }
    }

    [Fact]
    public async Task A_caller_whose_grants_do_not_cover_the_sensitive_field_is_refused()
    {
        await using var keyed = Keyed();
        using var fielded = keyed.CreateClient().SignedInAs("fia");

        using var response = await fielded.SendAsync(Request(Rows, HostA), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
