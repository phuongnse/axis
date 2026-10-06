using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordDeleteEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Departments = "/api/apps/RecordsApp/entities/Department/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delete_returns_204_and_the_record_is_gone_while_an_unknown_or_malformed_id_is_a_404()
    {
        await fixture.ResetAsync();
        var id = await CreateAsync(Items, HostA, """{ "name": "Desk" }""");

        using var response = await DeleteAsync($"{Items}/{id:D}", HostA);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken));
        using var get = Request($"{Items}/{id:D}", HostA);
        using var getResponse = await fixture.Client.SendAsync(get, CancellationToken);
        using var gone = await ReadProblemAsync(getResponse, HttpStatusCode.NotFound);

        foreach (var missing in new[] { Guid.NewGuid().ToString("D"), "not-a-guid", id.ToString("D") })
        {
            using var missingResponse = await DeleteAsync($"{Items}/{missing}", HostA);
            using var problem = await ReadProblemAsync(missingResponse, HttpStatusCode.NotFound);
            Assert.Equal("No record has this id.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task Delete_of_a_referenced_record_is_a_409_problem_and_keeps_the_record()
    {
        await fixture.ResetAsync();
        var department = await CreateAsync(Departments, HostA, """{ "name": "Sales" }""");
        await CreateAsync(Items, HostA, $$"""{ "name": "Desk", "department": "{{department:D}}" }""");

        using var response = await DeleteAsync($"{Departments}/{department:D}", HostA);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("Another record references this record.", problem.RootElement.GetProperty("title").GetString());
        using var record = await GetJsonAsync($"{Departments}/{department:D}", HostA);
        Assert.Equal("\"Sales\"", record.RootElement.GetProperty("values").GetProperty("name").GetRawText());
    }

    [Fact]
    public async Task Delete_through_another_tenant_host_is_a_404_and_keeps_the_record()
    {
        await fixture.ResetAsync();
        var id = await CreateAsync(Departments, HostA, """{ "name": "Sales" }""");

        using var response = await DeleteAsync($"{Departments}/{id:D}", HostB);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        using var record = await GetJsonAsync($"{Departments}/{id:D}", HostA);
        Assert.Equal("\"Sales\"", record.RootElement.GetProperty("values").GetProperty("name").GetRawText());
    }

    private async Task<Guid> CreateAsync(string path, string host, string values)
    {
        using var request = Request(HttpMethod.Post, path, host, $$"""{ "values": {{values}} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var record = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return record.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> DeleteAsync(string path, string host)
    {
        using var request = Request(HttpMethod.Delete, path, host);
        return await fixture.Client.SendAsync(request, CancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string host)
    {
        using var request = Request(path, host);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
