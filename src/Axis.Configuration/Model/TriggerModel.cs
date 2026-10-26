namespace Axis.Configuration.Model;

/// <summary>What starts a trigger.</summary>
public enum TriggerType
{
    /// <summary>A raised event.</summary>
    Event,

    /// <summary>A record of the process's subject entity that was created or updated, or a record of the entity that was deleted, which starts a process without a subject.</summary>
    DataChange,

    /// <summary>A cron schedule, for each row of a data source, or for a scheduled job without a data source, once.</summary>
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

    /// <summary>A record is deleted.</summary>
    Delete = 4,
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
/// or updated, or, for a delete trigger, a process without a subject for a record that was deleted. Data
/// change triggers fire on record API creates and updates and on an inbound endpoint's create. A delete
/// trigger fires on a delete through the record command. A schedule trigger starts it on a cron schedule
/// in its own time zone, for each row of a data source with fixed parameter values. The worker runs the
/// occurrences.
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

    /// <summary>The process the trigger starts. Its subject entity is the event's or the watched entity, and a delete trigger's process has none.</summary>
    public required ProcessReference Process { get; init; }

    /// <summary>The boolean condition over the event's payload fields or over the watched record, or null when every occurrence starts the process.</summary>
    public ExpressionModel? When { get; init; }

    /// <summary>
    /// The expressions that give the process's parameters from the deleted record, by declared name, in the
    /// declaration order of the process. It is set for a delete trigger only.
    /// </summary>
    public IReadOnlyDictionary<string, ExpressionModel>? ParameterExpressions { get; init; }

    /// <summary>The cron of a schedule trigger. It is set for a schedule trigger only.</summary>
    public CronSchedule? Schedule { get; init; }

    /// <summary>The time zone in which <see cref="Schedule"/> is read. It is set for a schedule trigger only.</summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>The data source whose rows a schedule trigger starts the process for. It is null for a scheduled job, which starts a process without a subject once, and for every other trigger.</summary>
    public DataSourceReference? DataSource { get; init; }

    /// <summary>
    /// The values of the data source's parameters, or of the process's parameters for a scheduled job, by declared
    /// name, in the declaration order of the data source or the process. A value is a <see cref="string"/> for text and enum, <see cref="long"/>, <see cref="decimal"/>,
    /// <see cref="bool"/>, <see cref="DateOnly"/>, a <see cref="DateTimeOffset"/> in UTC or a <see cref="Guid"/> for
    /// reference, or null when an optional parameter is not given. It is set for a schedule trigger only.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
}
