using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The matched reference of the RecordsApp fixture: a visit's <c>badge</c> is set by the server from its
/// match, so no record API body can set it.
/// </summary>
public sealed class RecordMatchedReferenceTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Visits = "/api/apps/RecordsApp/entities/Visit/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("\"7d1f3a52-9c40-4e86-8b17-2a5e6c0d4f93\"")]
    [InlineData("null")]
    public async Task Create_body_that_sets_the_matched_reference_is_400_at_that_field_and_writes_nothing(string badge)
    {
        await fixture.ResetAsync();

        using var response = await SendAsync(HttpMethod.Post, Visits, $$"""{ "values": { "holder": "Anna", "badge": {{badge}} } }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/badge", "Cannot be set.")], Errors(problem));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Visit")).RowCount);
    }

    [Theory]
    [InlineData("\"7d1f3a52-9c40-4e86-8b17-2a5e6c0d4f93\"")]
    [InlineData("null")]
    public async Task Update_body_that_sets_the_matched_reference_is_400_at_that_field_and_changes_nothing(string badge)
    {
        await fixture.ResetAsync();
        using var created = await SendAsync(HttpMethod.Post, Visits, """{ "values": { "holder": "Anna" } }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("id").GetString()!;

        using var response = await SendAsync(HttpMethod.Patch, $"{Visits}/{id}", $$"""{ "version": 1, "values": { "holder": "Ben", "badge": {{badge}} } }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([("/values/badge", "Cannot be set.")], Errors(problem));
        using var read = await SendAsync(HttpMethod.Get, $"{Visits}/{id}", null);
        using var record = JsonDocument.Parse(await read.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("Anna", record.RootElement.GetProperty("values").GetProperty("holder").GetString());
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body) =>
        fixture.Client.SendAsync(body is null ? Request(path, HostA) : Request(method, path, HostA, body), CancellationToken);

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
