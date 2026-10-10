using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The sequence field of the RecordsApp fixture: a ticket's <c>number</c> comes from the
/// <c>TicketNumber</c> sequence, <c>T-{yyyy}-{n:4}</c>. Counters are never reset, so each test
/// reads the last number handed out before it checks the next ones.
/// </summary>
public sealed partial class RecordSequenceEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Tickets = "/api/apps/RecordsApp/entities/Ticket/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Concurrent_creates_get_distinct_numbers_that_follow_on_with_no_gaps()
    {
        await fixture.ResetAsync();
        var first = await CreateAsync(HostA, """{ "title": "First" }""");

        var numbers = await Task.WhenAll(Enumerable.Range(1, 20).Select(index => CreateAsync(HostA, $$"""{ "title": "Ticket {{index}}" }""")));

        Assert.Equal(Enumerable.Range(1, 20).Select(offset => first + offset), numbers.Order());
    }

    [Fact]
    public async Task Refused_creates_use_no_number()
    {
        await fixture.ResetAsync();
        var first = await CreateAsync(HostA, """{ "title": "First", "code": "C-1" }""");

        using var invalid = await PostAsync(HostA, """{ "title": "Invalid", "priority": 0 }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/priority", "ticket.priorityPositive")], Errors(invalid));

        // The duplicate is refused by the insert, after the counter was incremented.
        using var duplicate = await PostAsync(HostA, """{ "title": "Duplicate", "code": "C-1" }""", HttpStatusCode.Conflict);
        Assert.Equal([("/values/code", "Must be unique.")], Errors(duplicate));

        Assert.Equal(first + 1, await CreateAsync(HostA, """{ "title": "Next" }"""));
        Assert.Equal(2, (await fixture.TableShapeAsync(TenantA, "Ticket")).RowCount);
    }

    [Fact]
    public async Task Each_tenant_counts_on_its_own()
    {
        await fixture.ResetAsync();
        var lastInB = await CreateAsync(HostB, """{ "title": "B" }""");
        var lastInA = await CreateAsync(HostA, """{ "title": "A" }""");

        for (var index = 1; index <= 3; index++)
        {
            Assert.Equal(lastInA + index, await CreateAsync(HostA, """{ "title": "A" }"""));
        }

        Assert.Equal(lastInB + 1, await CreateAsync(HostB, """{ "title": "B" }"""));
    }

    [Fact]
    public async Task Body_that_sets_the_sequence_field_is_400_at_that_field_and_writes_nothing()
    {
        await fixture.ResetAsync();

        using var create = await PostAsync(HostA, """{ "title": "Set", "number": "X" }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/number", "Cannot be set.")], Errors(create));
        using var createNull = await PostAsync(HostA, """{ "title": "Set", "number": null }""", HttpStatusCode.BadRequest);
        Assert.Equal([("/values/number", "Cannot be set.")], Errors(createNull));
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Ticket")).RowCount);

        using var created = await PostAsync(HostA, """{ "title": "Kept" }""", HttpStatusCode.Created);
        var id = created.RootElement.GetProperty("id").GetString()!;
        var number = NumberText(created);

        using var update = await PatchAsync(id, """{ "version": 1, "values": { "number": "X" } }""");
        Assert.Equal([("/values/number", "Cannot be set.")], Errors(update));
        using var updateNull = await PatchAsync(id, """{ "version": 1, "values": { "number": null } }""");
        Assert.Equal([("/values/number", "Cannot be set.")], Errors(updateNull));

        using var read = await GetJsonAsync($"{Tickets}/{id}");
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(number, NumberText(read));
    }

    /// <summary>Creates a ticket on <paramref name="host"/> and returns the number part of its business number.</summary>
    private async Task<long> CreateAsync(string host, string values)
    {
        using var created = await PostAsync(host, values, HttpStatusCode.Created);
        var match = NumberPattern().Match(NumberText(created));
        Assert.True(match.Success, $"'{NumberText(created)}' is not a ticket number.");
        Assert.Equal(DateTime.UtcNow.Year, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        return long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    }

    private static string NumberText(JsonDocument record) =>
        record.RootElement.GetProperty("values").GetProperty("number").GetString()!;

    private async Task<JsonDocument> PostAsync(string host, string values, HttpStatusCode expected)
    {
        using var request = Request(HttpMethod.Post, Tickets, host, $$"""{ "values": {{values}} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        if (expected != HttpStatusCode.Created)
        {
            return await ReadProblemAsync(response, expected);
        }

        Assert.Equal(expected, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<JsonDocument> PatchAsync(string id, string body)
    {
        using var request = Request(HttpMethod.Patch, $"{Tickets}/{id}", HostA, body);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        return await ReadProblemAsync(response, HttpStatusCode.BadRequest);
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    /// <summary>Each error key with its single message, in the problem's order.</summary>
    private static List<(string Key, string Message)> Errors(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(property => (property.Name, Assert.Single(property.Value.EnumerateArray()).GetString()!))
            .ToList();

    // [0-9] rather than \d, which also matches non-ASCII digits.
    [GeneratedRegex(@"^T-([0-9]{4})-([0-9]{4,})\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();
}
