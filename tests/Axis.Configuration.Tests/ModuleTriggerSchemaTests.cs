using System.Text.Json;
using Axis.Configuration.Loading;

namespace Axis.Configuration.Tests;

/// <summary>Pins the module copy of the trigger schema, which is built by editing the shared one.</summary>
public sealed class ModuleTriggerSchemaTests
{
    private const string Schedule = """
        { "id": "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53", "kind": "trigger", "name": "Nightly", "formatVersion": 1,
          "type": "schedule", "schedule": "0 6 * * *", "process": "Close" }
        """;

    private const string ScheduleWithTimeZone = """
        { "id": "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53", "kind": "trigger", "name": "Nightly", "formatVersion": 1,
          "type": "schedule", "schedule": "0 6 * * *", "timeZone": "Europe/Berlin", "process": "Close" }
        """;

    [Fact]
    public void A_module_trigger_schedule_needs_no_time_zone()
    {
        Assert.True(IsValid(ResourceSchemas.ModuleTrigger, Schedule));
    }

    [Fact]
    public void A_module_trigger_schedule_cannot_have_a_time_zone()
    {
        Assert.False(IsValid(ResourceSchemas.ModuleTrigger, ScheduleWithTimeZone));
    }

    [Fact]
    public void An_application_trigger_schedule_still_requires_a_time_zone()
    {
        Assert.True(ResourceSchemas.TryGet("trigger", out var schema));
        Assert.False(IsValid(schema, Schedule));
        Assert.True(IsValid(schema, ScheduleWithTimeZone));
    }

    private static bool IsValid(Json.Schema.JsonSchema schema, string json)
    {
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(document.RootElement).IsValid;
    }
}
