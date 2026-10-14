namespace Axis.Configuration.Model;

/// <summary>What starts a trigger.</summary>
public enum TriggerType
{
    /// <summary>A raised event.</summary>
    Event,

    /// <summary>A record of the process's subject entity that was created or updated.</summary>
    DataChange,

    /// <summary>A cron schedule, for each row of a data source.</summary>
    Schedule,
}

/// <summary>The kinds of write that start a data change trigger.</summary>
[Flags]
public enum DataChangeKinds
{
    None = 0,

    /// <summary>A record is created.</summary>
    Create = 1,

    /// <summary>A record is updated.</summary>
    Update = 2,
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
/// A compiled trigger: it starts a process when something happens. An event trigger starts the
/// process for the record a raised event names, either later in async mode or in the raising
/// step's transaction in sync mode. A data change trigger starts it for a record that was created
/// or updated. Data change triggers fire on record API creates and updates and on an inbound
/// endpoint's create. A schedule trigger starts it on a cron schedule in its own time zone, for each
/// row of a data source with fixed parameter values. The worker does not run schedule triggers yet.
/// </summary>
public sealed record TriggerModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The trigger's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    public required TriggerType Type { get; init; }

    /// <summary>How the process is started. It applies to event triggers only, and a data change trigger has <see cref="TriggerMode.Async"/>.</summary>
    public required TriggerMode Mode { get; init; }

    /// <summary>The event that starts the trigger. It is set for an event trigger.</summary>
    public EventReference? Event { get; init; }

    /// <summary>The entity whose records the trigger watches. It is set for a data change trigger and is the process's subject entity.</summary>
    public EntityReference? Entity { get; init; }

    /// <summary>The kinds of write that start the trigger. It is <see cref="DataChangeKinds.None"/> for an event trigger.</summary>
    public DataChangeKinds On { get; init; }

    /// <summary>The declared names of the fields whose change starts the trigger, or null when a change to any field does. It is set for a data change trigger only.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>The process the trigger starts. Its subject entity is the event's or the watched entity.</summary>
    public required ProcessReference Process { get; init; }

    /// <summary>The boolean condition over the event's payload fields or over the watched record, or null when every occurrence starts the process.</summary>
    public ExpressionModel? When { get; init; }

    /// <summary>The cron of a schedule trigger. It is set for a schedule trigger only.</summary>
    public CronSchedule? Schedule { get; init; }

    /// <summary>The time zone in which <see cref="Schedule"/> is read. It is set for a schedule trigger only.</summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>The data source whose rows a schedule trigger starts the process for. It is set for a schedule trigger only.</summary>
    public DataSourceReference? DataSource { get; init; }

    /// <summary>
    /// The values of the data source's parameters, by declared name, in the data source's declaration order. A
    /// value is a <see cref="string"/> for text and enum, <see cref="long"/>, <see cref="decimal"/>,
    /// <see cref="bool"/>, <see cref="DateOnly"/>, a <see cref="DateTimeOffset"/> in UTC or a <see cref="Guid"/> for
    /// reference, or null when an optional parameter is not given. It is set for a schedule trigger only.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
}
