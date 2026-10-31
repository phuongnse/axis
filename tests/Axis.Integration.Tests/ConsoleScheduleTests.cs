using System.Net;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The Schedules view of the operator console lists the schedule trigger <c>RemindFollowups</c> of the <c>followups</c>
/// module once for each of the two applications of <see cref="ModuleTriggerFixture"/>, each with its own occurrences.
/// The tests share one database. A test that activates another release puts the original one back, and each test checks
/// the active state only.
/// </summary>
public sealed class ConsoleScheduleTests(ModuleTriggerFixture fixture) : IClassFixture<ModuleTriggerFixture>
{
    private const string Path = "/api/console/schedules";

    private const string TriggerName = "followups/RemindFollowups";

    private static readonly DateTimeOffset _occurrence = DateTimeOffset.Parse("2026-01-01T05:00:00Z");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_operator_sees_the_last_and_next_occurrence_of_the_application_where_it_ran_and_no_last_occurrence_in_the_other()
    {
        var trigger = fixture.Steps.Model.Triggers.Single(candidate => candidate.Name == TriggerName).Id;
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        await DueAtAsync(trigger);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        var (status, body) = await GetAsync("operator", string.Empty);

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = body;
        Assert.Equal(["items"], body.RootElement.EnumerateObject().Select(property => property.Name));
        var items = Reminders(body);
        Assert.Equal(2, items.Count);
        var first = items.Single(item => item.GetProperty("applicationId").GetGuid() == ModuleTriggerFixture.AppId);
        var other = items.Single(item => item.GetProperty("applicationId").GetGuid() == ModuleTriggerFixture.OtherAppId);
        Assert.NotEqual(first.GetProperty("triggerId").GetGuid(), other.GetProperty("triggerId").GetGuid());
        Assert.Equal(
            ["applicationId", "application", "triggerId", "trigger", "process", "schedule", "timeZone", "lastOccurrenceAt", "nextOccurrenceAt"],
            first.EnumerateObject().Select(property => property.Name));

        Assert.Equal("ModuleTriggerApp", first.GetProperty("application").GetString());
        Assert.Equal("followups/Remind", first.GetProperty("process").GetString());
        Assert.Equal("0 6 * * *", first.GetProperty("schedule").GetString());
        Assert.Equal("Asia/Ho_Chi_Minh", first.GetProperty("timeZone").GetString());
        var state = Assert.Single(await fixture.Steps.TriggerSchedulesAsync(), row => row.TriggerId == first.GetProperty("triggerId").GetGuid());
        Assert.NotNull(state.LastOccurrenceAt);
        Assert.Equal(state.LastOccurrenceAt, first.GetProperty("lastOccurrenceAt").GetDateTimeOffset());
        Assert.Equal(state.NextOccurrenceAt, first.GetProperty("nextOccurrenceAt").GetDateTimeOffset());

        Assert.Equal("ModuleTriggerOtherApp", other.GetProperty("application").GetString());
        Assert.Equal("Europe/Berlin", other.GetProperty("timeZone").GetString());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("lastOccurrenceAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, other.GetProperty("nextOccurrenceAt").ValueKind);
    }

    [Fact]
    public async Task A_module_trigger_is_listed_once_for_each_application_and_the_application_filter_keeps_one()
    {
        var (_, all) = await GetAsync("operator", string.Empty);
        var (status, filtered) = await GetAsync("operator", $"?application={ModuleTriggerFixture.OtherAppId}");

        Assert.Equal(HttpStatusCode.OK, status);
        using var _ = all;
        using var __ = filtered;
        Assert.Equal(
            [ModuleTriggerFixture.AppId, ModuleTriggerFixture.OtherAppId],
            Reminders(all).Select(item => item.GetProperty("applicationId").GetGuid()).Order());
        var only = Assert.Single(filtered.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(ModuleTriggerFixture.OtherAppId, only.GetProperty("applicationId").GetGuid());
        Assert.Equal(TriggerName, only.GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task An_application_that_is_not_an_id_is_a_400_keyed_application()
    {
        var (status, body) = await GetAsync("operator", "?application=nope");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        using var _ = body;
        Assert.Equal(["application"], body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task A_user_without_the_operator_role_gets_403_and_a_request_without_a_session_gets_401()
    {
        var (status, body) = await GetAsync("outsider", string.Empty);
        body.Dispose();
        using var client = fixture.CreateClient();
        using var request = Request(Path, ModuleTriggerFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_trigger_that_a_newer_release_removed_is_no_longer_listed()
    {
        using var folder = new TemporaryFolder();
        var source = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleTriggerApp");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            folder.With(System.IO.Path.GetRelativePath(source, file), await File.ReadAllTextAsync(file, CancellationToken));
        }

        folder
            .With("processes/tally.json", """
                {
                  "id": "7e2b9c40-3d15-4a86-b1f7-5c0d8a6e2f93",
                  "kind": "process",
                  "name": "Tally",
                  "formatVersion": 1,
                  "parameters": [{ "name": "days", "type": "integer" }],
                  "start": "done",
                  "steps": [{ "name": "done", "type": "end" }]
                }
                """)
            .With("triggers/tally-daily.json", """
                {
                  "id": "d4a6f1b8-9e23-4c57-8b0a-3f7e1c5d9a28",
                  "kind": "trigger",
                  "name": "TallyDaily",
                  "formatVersion": 1,
                  "type": "schedule",
                  "schedule": "0 6 * * *",
                  "timeZone": "Europe/Berlin",
                  "process": "Tally",
                  "parameters": { "days": 3 }
                }
                """);
        await fixture.Steps.ActivateAsync(folder.Path, []);

        var (_, listed) = await GetAsync("operator", string.Empty);
        using (listed)
        {
            var tally = Assert.Single(Triggers(listed, "TallyDaily"));
            Assert.Equal(ModuleTriggerFixture.AppId, tally.GetProperty("applicationId").GetGuid());
            Assert.Equal("Tally", tally.GetProperty("process").GetString());
        }

        await fixture.Steps.ActivateAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleTriggerApp"), []);

        var (_, removed) = await GetAsync("operator", string.Empty);
        using (removed)
        {
            Assert.Empty(Triggers(removed, "TallyDaily"));
            Assert.Equal(2, Reminders(removed).Count);
        }
    }

    private static List<JsonElement> Reminders(JsonDocument body) => Triggers(body, TriggerName);

    private static List<JsonElement> Triggers(JsonDocument body, string name) =>
        body.RootElement.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("trigger").GetString() == name).ToList();

    private async Task<(HttpStatusCode Status, JsonDocument Body)> GetAsync(string user, string query)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request(Path + query, ModuleTriggerFixture.Host);
        using var response = await client.SendAsync(request, CancellationToken);
        return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)));
    }

    /// <summary>Makes the stored occurrence and the schedule item of the trigger due at an old time.</summary>
    private async Task DueAtAsync(Guid trigger)
    {
        await fixture.Steps.ScalarAsync("UPDATE axis.trigger_schedules SET next_occurrence_at = @at WHERE trigger_id = @trigger", ("at", _occurrence), ("trigger", trigger));
        await fixture.Steps.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = @at WHERE kind = @kind AND trigger_id = @trigger",
            ("at", _occurrence),
            ("kind", ProcessStarts.TriggerScheduleWorkItemKind),
            ("trigger", trigger));
    }
}
