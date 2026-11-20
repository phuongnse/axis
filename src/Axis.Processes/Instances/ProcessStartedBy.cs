using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Processes.Instances;

/// <summary>
/// Who or what started a process instance, as "Who started it" in
/// <c>docs/reference/process-inspection.md#instance-detail</c> says. The start writes it as JSON text in
/// <c>process_instances.started_by</c>, always with all four keys, and the detail returns it.
/// </summary>
/// <param name="Kind"><c>person</c>, <c>trigger</c>, <c>endpoint</c>, <c>schedule</c> or <c>parent</c>.</param>
/// <param name="Name">The trigger's, endpoint's or parent process's name, or <see langword="null"/> for a person, whose name the detail reads from their profile.</param>
/// <param name="Id">The user's id for a person, the parent instance's id for a parent, otherwise <see langword="null"/>.</param>
/// <param name="Via">The label text key of the action a person started from, or <see langword="null"/>.</param>
public sealed record ProcessStartedBy(string Kind, string? Name, string? Id, string? Via)
{
    public const string PersonKind = "person";

    public const string TriggerKind = "trigger";

    public const string EndpointKind = "endpoint";

    public const string ScheduleKind = "schedule";

    public const string ParentKind = "parent";

    /// <summary>A user or service account who started the instance, from the action whose label text key is <paramref name="via"/>, if any.</summary>
    public static ProcessStartedBy Person(string userId, string? via) => new(PersonKind, null, userId, via);

    /// <summary>A data change or event trigger.</summary>
    public static ProcessStartedBy Trigger(string name) => new(TriggerKind, name, null, null);

    /// <summary>An inbound endpoint.</summary>
    public static ProcessStartedBy Endpoint(string name) => new(EndpointKind, name, null, null);

    /// <summary>A schedule trigger.</summary>
    public static ProcessStartedBy Schedule(string name) => new(ScheduleKind, name, null, null);

    /// <summary>A sub-process step of the instance <paramref name="instanceId"/> of the process <paramref name="process"/>.</summary>
    public static ProcessStartedBy Parent(string process, Guid instanceId) => new(ParentKind, process, instanceId.ToString(), null);

    /// <summary>The JSON text that is stored, with all four keys.</summary>
    public string ToJson() =>
        new JsonObject { ["kind"] = Kind, ["name"] = Name, ["id"] = Id, ["via"] = Via }.ToJsonString();

    /// <summary>Reads the stored JSON text, or returns <see langword="null"/> for an instance that has none.</summary>
    public static ProcessStartedBy? Parse(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new ProcessStartedBy(root.GetProperty("kind").GetString()!, Text(root, "name"), Text(root, "id"), Text(root, "via"));
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
