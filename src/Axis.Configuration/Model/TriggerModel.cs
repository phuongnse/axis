namespace Axis.Configuration.Model;

/// <summary>What starts a trigger.</summary>
public enum TriggerType
{
    /// <summary>A raised event.</summary>
    Event,
}

/// <summary>How a trigger starts its process.</summary>
public enum TriggerMode
{
    /// <summary>The worker delivers the occurrence later, and starts the process at most once per occurrence and trigger.</summary>
    Async,

    /// <summary>The <c>raiseEvent</c> step that raises the event starts the process in its own transaction, so both commit or neither does.</summary>
    Sync,
}

/// <summary>
/// A compiled trigger: it starts a process when something happens. For now every trigger is an
/// event trigger, which starts the process for the record a raised event names, either later in
/// async mode or in the raising step's transaction in sync mode.
/// </summary>
public sealed record TriggerModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The trigger's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required TriggerType Type { get; init; }

    public required TriggerMode Mode { get; init; }

    /// <summary>The event that starts the trigger. It is set for an event trigger, which every trigger is for now.</summary>
    public EventReference? Event { get; init; }

    /// <summary>The process the trigger starts. Its subject entity is the event's entity.</summary>
    public required ProcessReference Process { get; init; }

    /// <summary>The boolean condition over the event's payload fields, or null when every occurrence starts the process.</summary>
    public ExpressionModel? When { get; init; }
}
