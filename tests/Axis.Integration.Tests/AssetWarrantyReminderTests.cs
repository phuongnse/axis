using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>The asset management sample on tenant <c>a</c>, activated with its warranty reminder schedule.</summary>
public sealed class AssetWarrantyReminderFixture : IAsyncLifetime
{
    private readonly ProcessStepFixture _tenant = new("a", Path.Combine(AppContext.BaseDirectory, "Samples", "asset-management"));

    public ProcessStepFixture Tenant => _tenant;

    public ValueTask InitializeAsync() => _tenant.InitializeAsync();

    public ValueTask DisposeAsync() => _tenant.DisposeAsync();
}

/// <summary>
/// The warranty reminder of the asset management sample. The schedule runs on weekdays at 08:00 in
/// <c>Asia/Ho_Chi_Minh</c> and starts <c>WarrantyReminder</c> for each asset whose warranty ends within 30 days
/// and that is not retired. The reminder notifies the asset managers in-app and opens the asset.
/// </summary>
public sealed class AssetWarrantyReminderTests(AssetWarrantyReminderFixture fixture) : IClassFixture<AssetWarrantyReminderFixture>
{
    private static readonly DateTimeOffset _occurrence = DateTimeOffset.Parse("2026-01-05T01:00:00Z");

    private static readonly TimeZoneInfo _hoChiMinh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_schedule_is_due_on_a_weekday_at_eight_in_the_application_time_zone()
    {
        var state = Assert.Single(await fixture.Tenant.TriggerSchedulesAsync());

        var next = TimeZoneInfo.ConvertTime(state.NextOccurrenceAt, _hoChiMinh);
        Assert.InRange(next.DayOfWeek, DayOfWeek.Monday, DayOfWeek.Friday);
        Assert.Equal(new TimeSpan(8, 0, 0), next.TimeOfDay);
    }

    [Fact]
    public async Task An_occurrence_starts_one_reminder_for_the_asset_due_within_30_days_and_notifies_only_the_asset_managers()
    {
        var tenant = fixture.Tenant;
        Assert.True(tenant.Model.TryGetEntity("Asset", out var asset));
        Assert.True(tenant.Model.TryGetProcess("WarrantyReminder", out var process));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _hoChiMinh).DateTime);
        var dueEnd = today.AddDays(10);
        var due = await InsertAssetAsync(tenant, asset, "Laptop due", "in-stock", dueEnd);
        await InsertAssetAsync(tenant, asset, "Laptop later", "in-stock", today.AddDays(40));
        await InsertAssetAsync(tenant, asset, "Old laptop", "retired", today.AddDays(5));

        await tenant.ScalarAsync("UPDATE axis.trigger_schedules SET next_occurrence_at = @at", ("at", _occurrence));
        await tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = @at WHERE kind = @kind", ("at", _occurrence), ("kind", ProcessStarts.TriggerScheduleWorkItemKind));

        await tenant.RunUntilIdleAsync(CancellationToken);

        await using (var command = tenant.DataSource.CreateCommand("SELECT subject_id, state FROM axis.process_instances WHERE process_id = @process"))
        {
            command.Parameters.AddWithValue("process", process.Id);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            var instances = new List<(Guid SubjectId, string State)>();
            while (await reader.ReadAsync(CancellationToken))
            {
                instances.Add((reader.GetGuid(0), reader.GetString(1)));
            }

            Assert.Equal([(due, ProcessStarts.Completed)], instances);
        }

        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.notifications"));

        await using var connection = await tenant.DataSource.OpenConnectionAsync(CancellationToken);
        var applicationId = tenant.Model.Manifest.Id;
        var managerPage = await NotificationQueries.ListAsync(connection, applicationId, "quan", ["asset-manager"], false, 1, 50, CancellationToken);
        var otherManagerPage = await NotificationQueries.ListAsync(connection, applicationId, "linh", ["asset-manager"], false, 1, 50, CancellationToken);
        foreach (var page in new[] { managerPage, otherManagerPage })
        {
            var item = Assert.Single(page.Items);
            Assert.Equal(1L, page.TotalCount);
            Assert.Equal(("asset.warrantyReminder.subject", "asset.warrantyReminder.body", "AssetDetail"), (item.SubjectKey, item.BodyKey, item.OpenPage));
            var values = Assert.IsType<JsonObject>(JsonNode.Parse(item.Values));
            Assert.Equal(("Laptop due", dueEnd.ToString("yyyy-MM-dd")), (values["name"]!.GetValue<string>(), values["warrantyEnd"]!.GetValue<string>()));
            Assert.Equal(due, Guid.Parse(JsonNode.Parse(item.OpenParameters!)!["asset"]!.GetValue<string>()));
        }

        foreach (var (user, roles) in new[] { ("thao", new[] { "auditor" }), ("lan", new[] { "employee" }) })
        {
            var page = await NotificationQueries.ListAsync(connection, applicationId, user, roles, false, 1, 50, CancellationToken);
            Assert.Equal((0, 0L), (page.Items.Count, page.TotalCount));
        }
    }

    private static async Task<Guid> InsertAssetAsync(ProcessStepFixture tenant, EntityModel asset, string name, string status, DateOnly warrantyEnd)
    {
        var id = Guid.NewGuid();
        await using var command = tenant.DataSource.CreateCommand(
            $"INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(asset.Id))} (id, f_name, f_status, f_currency, f_warrantyend) " +
            "VALUES (@id, @name, @status, 'VND', @warrantyEnd)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("warrantyEnd", warrantyEnd);
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
