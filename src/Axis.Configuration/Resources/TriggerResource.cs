namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>trigger</c> resource: starts a process when something happens. An <c>event</c> trigger, in
/// <c>async</c> or <c>sync</c> mode, starts the process for the record a raised event names. A
/// <c>dataChange</c> trigger starts it for a record of the process's subject entity that was created
/// or updated. Its event or entity, its process and its <see cref="When"/> are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record TriggerResource : Resource
{
    /// <summary>What starts the trigger: <c>event</c> or <c>dataChange</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The name of the event that starts the trigger. The schema requires it for an <c>event</c> trigger.</summary>
    public string? Event { get; init; }

    /// <summary>The name of the entity whose records the trigger watches. The schema requires it for a <c>dataChange</c> trigger.</summary>
    public string? Entity { get; init; }

    /// <summary>The kinds of write that start the trigger, <c>create</c> and <c>update</c>. The schema requires a non-empty, unique list for a <c>dataChange</c> trigger.</summary>
    public IReadOnlyList<string>? On { get; init; }

    /// <summary>The fields whose change starts the trigger on an update, or null when a change to any field does. The schema accepts a non-empty, unique list.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>
    /// A boolean expression over the event's payload fields for an event trigger, or over the watched record
    /// for a data change trigger. Null means every occurrence starts the process.
    /// </summary>
    public string? When { get; init; }

    /// <summary>How the process is started. The schema accepts <c>async</c> or <c>sync</c> for an event trigger only, and null means <c>async</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>The name of the process the trigger starts.</summary>
    public required string Process { get; init; }
}
