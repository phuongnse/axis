using System.Globalization;
using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The defaults that the form state endpoint returns for a new record, on the <c>ReminderEntry</c>
/// form of the RecordsApp fixture. A reminder's <c>state</c> defaults to <c>open</c>, its
/// <c>priority</c> to 3 and its <c>remindAt</c> to <c>now()</c>, and <c>remindAt</c> is visible when
/// the priority is above 2. <c>fia</c> reads only a reminder's <c>title</c> and <c>state</c>.
/// </summary>
public sealed class FormStateDefaultTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string State = "/api/apps/RecordsApp/forms/ReminderEntry/state";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_record_gets_the_default_of_each_left_out_field_and_conditions_see_them()
    {
        using var state = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "title": "Call" }, "touched": [] }""");

        var defaults = state.RootElement.GetProperty("defaults");
        Assert.Equal(["state", "priority", "remindAt"], defaults.EnumerateObject().Select(property => property.Name));
        Assert.Equal("open", defaults.GetProperty("state").GetString());
        Assert.Equal(3, defaults.GetProperty("priority").GetInt64());
        Assert.True(DateTimeOffset.TryParse(defaults.GetProperty("remindAt").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _));
        Assert.True(state.RootElement.GetProperty("fields").GetProperty("remindAt").GetProperty("visible").GetBoolean());
    }

    [Fact]
    public async Task Field_that_the_body_sends_is_not_in_defaults_and_its_value_drives_the_conditions()
    {
        using var state = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "title": "Call", "priority": 1, "state": null }, "touched": [] }""");

        var defaults = state.RootElement.GetProperty("defaults");
        Assert.Equal(["remindAt"], defaults.EnumerateObject().Select(property => property.Name));
        Assert.False(state.RootElement.GetProperty("fields").GetProperty("remindAt").GetProperty("visible").GetBoolean());
    }

    [Fact]
    public async Task Field_with_a_value_that_does_not_parse_gets_no_default()
    {
        using var state = await PostAsync(fixture.Client, """{ "recordId": null, "values": { "title": "Call", "priority": "high" }, "touched": ["priority"] }""");

        Assert.False(state.RootElement.GetProperty("defaults").TryGetProperty("priority", out _));
        Assert.Single(state.RootElement.GetProperty("errors").GetProperty("/values/priority").EnumerateArray());
    }

    [Fact]
    public async Task Stored_record_gets_no_defaults()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Reminder", new Dictionary<string, string?> { ["title"] = "Call", ["state"] = "closed", ["priority"] = "1" });

        using var state = await PostAsync(fixture.Client, $$"""{ "recordId": "{{id:D}}", "values": {}, "touched": [] }""");

        var defaults = state.RootElement.GetProperty("defaults");
        Assert.Equal(JsonValueKind.Object, defaults.ValueKind);
        Assert.Empty(defaults.EnumerateObject());
    }

    [Fact]
    public async Task Field_that_the_user_may_not_read_is_not_in_defaults()
    {
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using var state = await PostAsync(fia, """{ "recordId": null, "values": { "title": "Call" }, "touched": [] }""");

        var defaults = state.RootElement.GetProperty("defaults");
        Assert.Equal(["state"], defaults.EnumerateObject().Select(property => property.Name));
        Assert.Equal("open", defaults.GetProperty("state").GetString());
    }

    private static async Task<JsonDocument> PostAsync(HttpClient client, string body)
    {
        using var request = Request(HttpMethod.Post, State, HostA, body);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
