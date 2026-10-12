namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>trigger</c> resource: starts a process when something happens. For now the schema accepts
/// only an <c>event</c> trigger in <c>async</c> mode, which starts the process for the record a
/// raised event names. Its event, its process and its <see cref="When"/> are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record TriggerResource : Resource
{
    /// <summary>What starts the trigger. For now the schema accepts only <c>event</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The name of the event that starts the trigger. The schema requires it for an <c>event</c> trigger.</summary>
    public string? Event { get; init; }

    /// <summary>A boolean expression over the event's payload fields, or null when every occurrence starts the process.</summary>
    public string? When { get; init; }

    /// <summary>How the process is started. For now the schema accepts only <c>async</c>, and null means <c>async</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>The name of the process the trigger starts.</summary>
    public required string Process { get; init; }
}
