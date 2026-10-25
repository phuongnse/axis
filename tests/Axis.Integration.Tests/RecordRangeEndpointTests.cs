using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The ranges of the RecordsApp fixture's Reading entity: a value outside <c>min</c>, <c>max</c> or
/// <c>minLength</c> is a 400 at its field, and a value on a bound is written.
/// </summary>
public sealed class RecordRangeEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Readings = "/api/apps/RecordsApp/entities/Reading/records";

    private const string OnTheBounds = """{ "count": 10, "amount": 0.5, "takenOn": "2026-12-31", "code": "abc" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Refused => new()
    {
        { """{ "count": 0 }""", "/values/count|Must be at least 1." },
        { """{ "count": 11 }""", "/values/count|Must be at most 10." },
        { """{ "amount": 0.49 }""", "/values/amount|Must be at least 0.5." },
        { """{ "amount": 99.51 }""", "/values/amount|Must be at most 99.5." },
        { """{ "takenOn": "2025-12-31" }""", "/values/takenOn|Must be at least 2026-01-01." },
        { """{ "takenOn": "2027-01-01" }""", "/values/takenOn|Must be at most 2026-12-31." },
        { """{ "code": "ab" }""", "/values/code|Must be at least 3 characters." },
        { """{ "code": "abcdefghijk" }""", "/values/code|Must be at most 10 characters." },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Create_outside_a_limit_is_400_at_the_field_and_writes_nothing(string values, string expected)
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var problem = await SendAsync(anna, HttpMethod.Post, Readings, $$"""{ "values": {{values}} }""", HttpStatusCode.BadRequest);

        Assert.Equal([Split(expected)], Errors(problem));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Reading")).RowCount);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Update_outside_a_limit_is_400_at_the_field_and_keeps_the_record(string values, string expected)
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var created = await SendAsync(
            anna, HttpMethod.Post, Readings, $$"""{ "values": {{OnTheBounds}} }""", HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString();

        using var problem = await SendAsync(
            anna, HttpMethod.Patch, $"{Readings}/{id}", $$"""{ "version": 1, "values": {{values}} }""", HttpStatusCode.BadRequest);

        Assert.Equal([Split(expected)], Errors(problem));
        using var read = await SendAsync(anna, HttpMethod.Get, $"{Readings}/{id}", null, HttpStatusCode.OK);
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("abc", read.RootElement.GetProperty("values").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Values_on_each_bound_are_written_on_create_and_update()
    {
        await fixture.ResetAsync();
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var upper = await SendAsync(
            anna, HttpMethod.Post, Readings, $$"""{ "values": {{OnTheBounds}} }""", HttpStatusCode.Created);
        var id = upper.RootElement.GetProperty("id").GetString();
        using var lower = await SendAsync(
            anna,
            HttpMethod.Patch,
            $"{Readings}/{id}",
            """{ "version": 1, "values": { "count": 1, "amount": 99.5, "takenOn": "2026-01-01", "code": "abcdefghij" } }""",
            HttpStatusCode.OK);

        var values = lower.RootElement.GetProperty("values");
        Assert.Equal(1, values.GetProperty("count").GetInt64());
        Assert.Equal(99.5m, values.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-01-01", values.GetProperty("takenOn").GetString());
        Assert.Equal("abcdefghij", values.GetProperty("code").GetString());
        Assert.Equal(1, (await fixture.TableShapeAsync(TenantA, "Reading")).RowCount);
    }

    private static (string Key, string Message) Split(string expected)
    {
        var parts = expected.Split('|');
        return (parts[0], parts[1]);
    }

    private static async Task<JsonDocument> SendAsync(
        HttpClient client, HttpMethod method, string path, string? body, HttpStatusCode expected)
    {
        using var request = Request(method, path, HostA, body);
        using var response = await client.SendAsync(request, CancellationToken);
        if (expected == HttpStatusCode.BadRequest)
        {
            return await ReadProblemAsync(response, expected);
        }

        Assert.Equal(expected, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();
}
