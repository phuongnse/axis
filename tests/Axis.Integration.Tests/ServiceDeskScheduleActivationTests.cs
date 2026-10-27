using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The service desk sample runs in <c>Asia/Ho_Chi_Minh</c> and its morning reminder is a schedule trigger.
/// This class has its own database, because activating the whole sample applies its migration, which
/// <see cref="ServiceDeskActivationTests"/> starts from a release without.
/// </summary>
public sealed class ServiceDeskScheduleActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "service-desk");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_morning_reminder_is_a_weekday_schedule_at_eight_in_the_application_time_zone()
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(_appFolder, context, SamplePackages.Versions, CancellationToken);
        Assert.Empty(compiled.Diagnostics);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using (var configuration = DataDatabaseFixture.CreateConfigurationContext(connection))
        await using (var data = DataDatabaseFixture.CreateContext(connection))
        {
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("acme"), OpenInstanceCounter.Instance, CancellationToken);
            Assert.Empty(activated.Diagnostics);
        }

        await TriggerSchedules.ActivateAsync(connection, "acme", compiled.Model!, compiled.Release!.Id, CancellationToken);

        var triggerId = Guid.Parse(JsonNode.Parse(File.ReadAllText(Path.Combine(_appFolder, "triggers", "every-morning.json")))!["id"]!.GetValue<string>());
        await using var command = new NpgsqlCommand("SELECT schedule, time_zone, next_occurrence_at FROM axis.trigger_schedules WHERE trigger_id = @id", connection);
        command.Parameters.AddWithValue("id", triggerId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal("0 8 * * 1-5", reader.GetString(0));
        Assert.Equal("Asia/Ho_Chi_Minh", reader.GetString(1));
        var next = TimeZoneInfo.ConvertTime(reader.GetFieldValue<DateTimeOffset>(2), TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        Assert.InRange(next.DayOfWeek, DayOfWeek.Monday, DayOfWeek.Friday);
        Assert.Equal(new TimeSpan(8, 0, 0), next.TimeOfDay);
    }
}
