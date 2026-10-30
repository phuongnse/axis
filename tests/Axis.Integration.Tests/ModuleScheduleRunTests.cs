using System.Net;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The schedule trigger <c>RemindFollowups</c> of the <c>followups</c> module starts <c>Remind</c> at 06:00
/// for each record of its data source <c>AllFollowups</c>. Each application has its own copy of the
/// trigger, with its own state and work item, and runs it in its own time zone: ModuleTriggerApp in
/// <c>Asia/Ho_Chi_Minh</c> and ModuleTriggerOtherApp in <c>Europe/Berlin</c>. A test sets the stored
/// occurrence in the past instead of waiting for it. The tests share one database and each checks
/// the records and rows of its own.
/// </summary>
public sealed class ModuleScheduleRunTests(ModuleTriggerFixture fixture) : IClassFixture<ModuleTriggerFixture>
{
    private const string App = "ModuleTriggerApp";

    private const string OtherApp = "ModuleTriggerOtherApp";

    private const string TriggerName = "followups/RemindFollowups";

    private static readonly DateTimeOffset _occurrence = DateTimeOffset.Parse("2026-01-01T05:00:00Z");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private ApplicationModel Model => fixture.Steps.Model;

    [Fact]
    public async Task Two_applications_fire_the_module_schedule_at_06_00_in_their_own_time_zone_each_with_its_own_state()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");
        var first = await CreateFollowupAsync(client, App, $"First {Guid.NewGuid():N}");
        var second = await CreateFollowupAsync(client, OtherApp, $"Second {Guid.NewGuid():N}");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var trigger = TriggerId(Model);
        var otherTrigger = TriggerId(fixture.OtherModel);
        Assert.NotEqual(trigger, otherTrigger);
        await DueAtAsync(trigger);
        await DueAtAsync(otherTrigger);

        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        var state = await StateAsync(trigger);
        var otherState = await StateAsync(otherTrigger);
        Assert.Equal(Model.Manifest.Id, state.ApplicationId);
        Assert.Equal(fixture.OtherModel.Manifest.Id, otherState.ApplicationId);
        var occurrence = state.LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran.");
        var otherOccurrence = otherState.LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran.");
        AssertSixOClock("Asia/Ho_Chi_Minh", occurrence);
        AssertSixOClock("Europe/Berlin", otherOccurrence);
        Assert.NotEqual(occurrence, otherOccurrence);
        AssertNext("Asia/Ho_Chi_Minh", state.NextOccurrenceAt);
        AssertNext("Europe/Berlin", otherState.NextOccurrenceAt);
        Assert.Equal(state.NextOccurrenceAt, (await fixture.Steps.ScheduleItemsAsync()).Single(item => item.TriggerId == trigger).DueAt);
        Assert.Equal(otherState.NextOccurrenceAt, (await fixture.Steps.ScheduleItemsAsync()).Single(item => item.TriggerId == otherTrigger).DueAt);

        // Each application starts its own copy of the process for the records of its own copy only.
        Assert.Equal(1L, await fixture.InstanceCountAsync(Model, "followups/Remind", first));
        Assert.Equal(0L, await fixture.InstanceCountAsync(Model, "followups/Remind", second));
        Assert.Equal(1L, await fixture.InstanceCountAsync(fixture.OtherModel, "followups/Remind", second));
        Assert.Equal(0L, await fixture.InstanceCountAsync(fixture.OtherModel, "followups/Remind", first));
        Assert.Equal(1L, await ReceiptCountAsync(trigger, first));
        Assert.Equal(1L, await ReceiptCountAsync(otherTrigger, second));
        Assert.Equal(0L, await ReceiptCountAsync(trigger, second));
        Assert.Equal(0L, await ReceiptCountAsync(otherTrigger, first));
    }

    [Fact]
    public async Task A_missed_occurrence_of_one_application_runs_without_firing_the_schedule_of_the_other()
    {
        using var client = fixture.CreateClient().SignedInAs("clerk");
        var trigger = TriggerId(Model);
        var otherTrigger = TriggerId(fixture.OtherModel);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var followup = await CreateFollowupAsync(client, App, $"Missed {Guid.NewGuid():N}");
        var otherFollowup = await CreateFollowupAsync(client, OtherApp, $"Untouched {Guid.NewGuid():N}");
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        var otherState = await StateAsync(otherTrigger);
        var otherItem = (await fixture.Steps.ScheduleItemsAsync()).Single(item => item.TriggerId == otherTrigger);

        await DueAtAsync(trigger);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);
        await fixture.Steps.RunUntilIdleAsync(CancellationToken);

        var state = await StateAsync(trigger);
        AssertSixOClock("Asia/Ho_Chi_Minh", state.LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran."));
        Assert.True(state.NextOccurrenceAt > DateTimeOffset.UtcNow);
        Assert.Equal(1L, await fixture.InstanceCountAsync(Model, "followups/Remind", followup));
        Assert.Equal(1L, await ReceiptCountAsync(trigger, followup));
        Assert.Equal(otherState, await StateAsync(otherTrigger));
        Assert.Equal(otherItem, (await fixture.Steps.ScheduleItemsAsync()).Single(item => item.TriggerId == otherTrigger));
        Assert.Equal(0L, await fixture.InstanceCountAsync(fixture.OtherModel, "followups/Remind", otherFollowup));
        Assert.Equal(0L, await ReceiptCountAsync(otherTrigger, otherFollowup));
    }

    private static Guid TriggerId(ApplicationModel model) => model.Triggers.Single(trigger => trigger.Name == TriggerName).Id;

    private static void AssertSixOClock(string timeZoneId, DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        Assert.Equal((6, 0, 0), (local.Hour, local.Minute, local.Second));
    }

    private static void AssertNext(string timeZoneId, DateTimeOffset next)
    {
        AssertSixOClock(timeZoneId, next);
        Assert.True(next > DateTimeOffset.UtcNow);

        // The next 06:00 is a day away at most, or 25 hours on the day the clocks go back.
        Assert.True(next <= DateTimeOffset.UtcNow.AddHours(25));
    }

    private async Task<TriggerScheduleState> StateAsync(Guid trigger) =>
        Assert.Single(await fixture.Steps.TriggerSchedulesAsync(), state => state.TriggerId == trigger);

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

    private Task<long> ReceiptCountAsync(Guid trigger, Guid subject) =>
        fixture.Steps.CountAsync(
            "SELECT count(*) FROM axis.trigger_starts WHERE trigger_id = @trigger AND subject_id = @subject AND outcome = 'started'",
            ("trigger", trigger),
            ("subject", subject));

    private static async Task<Guid> CreateFollowupAsync(HttpClient client, string app, string title)
    {
        using var request = Request(
            HttpMethod.Post,
            $"/api/apps/{app}/entities/followups/Followup/records",
            ModuleTriggerFixture.Host,
            $$"""{ "values": { "title": "{{title}}" } }""");
        using var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
