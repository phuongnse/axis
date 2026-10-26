using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The validations of a <c>Workstation</c> of the RecordsApp fixture aggregate over its
/// <c>software</c> set of <c>Product</c> ids: <c>count(software) &lt;= 2</c> and
/// <c>seats == null or count(software) &lt;= seats</c>. A write reads the target records of the sent
/// ids, or of the stored set when an update leaves the field out.
/// </summary>
public sealed class RecordSetValidationEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Workstations = "/api/apps/RecordsApp/entities/Workstation/records";
    private const string Products = "/api/apps/RecordsApp/entities/Product/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_is_refused_when_the_sent_set_fails_a_validation_and_accepted_when_it_holds()
    {
        await fixture.ResetAsync();
        var office = await CreateProductAsync("Office");
        var paint = await CreateProductAsync("Paint");
        var editor = await CreateProductAsync("Editor");

        using var refused = await SendAsync(
            HttpMethod.Post,
            Workstations,
            $$"""{ "values": { "name": "Desk 1", "software": ["{{office:D}}", "{{paint:D}}", "{{editor:D}}"] } }""");
        using var problem = await ReadProblemAsync(refused, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/software", error.Name);
        Assert.Equal("workstation.tooMuchSoftware", error.Value[0].GetString());
        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {WorkstationTable}"));

        using var accepted = await SendAsync(
            HttpMethod.Post,
            Workstations,
            $$"""{ "values": { "name": "Desk 1", "software": ["{{office:D}}", "{{paint:D}}"] } }""");
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        // A create without the field has an empty set.
        using var empty = await SendAsync(HttpMethod.Post, Workstations, """{ "values": { "name": "Desk 2", "seats": 0 } }""");
        Assert.Equal(HttpStatusCode.Created, empty.StatusCode);
    }

    [Fact]
    public async Task An_update_that_leaves_the_set_out_is_validated_against_the_stored_set()
    {
        await fixture.ResetAsync();
        var office = await CreateProductAsync("Office");
        var paint = await CreateProductAsync("Paint");
        using var created = await SendAsync(
            HttpMethod.Post,
            Workstations,
            $$"""{ "values": { "name": "Desk 1", "software": ["{{office:D}}", "{{paint:D}}"] } }""");
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = createdBody.RootElement.GetProperty("id").GetString()!;

        using var refused = await SendAsync(HttpMethod.Patch, $"{Workstations}/{id}", """{ "version": 1, "values": { "seats": 1 } }""");
        using var problem = await ReadProblemAsync(refused, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/values/seats", error.Name);
        Assert.Equal("workstation.tooFewSeats", error.Value[0].GetString());

        // Sending a smaller set in the same body is read as sent, not as stored.
        using var accepted = await SendAsync(
            HttpMethod.Patch, $"{Workstations}/{id}", $$"""{ "version": 1, "values": { "seats": 1, "software": ["{{office:D}}"] } }""");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    private string WorkstationTable
    {
        get
        {
            Assert.True(fixture.Model.TryGetEntity("Workstation", out var entity));
            return EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));
        }
    }

    private async Task<Guid> CreateProductAsync(string name)
    {
        using var response = await SendAsync(HttpMethod.Post, Products, $$"""{ "values": { "name": "{{name}}" } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string body)
    {
        using var request = Request(method, path, HostA, body);
        return await fixture.Client.SendAsync(request, CancellationToken);
    }
}
