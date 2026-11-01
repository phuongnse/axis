using Microsoft.Extensions.Logging;

namespace Axis.Processes.Records;

/// <summary>
/// The log line of a write that a module entity's lifecycle refused. It names the entity, the point of
/// the contract and the record, and never holds a field value or a state.
/// </summary>
internal static partial class RecordLifecycleLog
{
    [LoggerMessage(LogLevel.Information, "A write of entity {EntityId} was refused by its lifecycle at {Point} for record {RecordId}.")]
    private static partial void Refused(ILogger logger, Guid entityId, string point, Guid? recordId);

    /// <summary>Logs a refusal. A create has no <paramref name="recordId"/>, and a refusal with no named point logs <c>no transition</c>.</summary>
    public static void Refused(ILoggerFactory loggerFactory, Guid entityId, string? point, Guid? recordId) =>
        Refused(loggerFactory.CreateLogger(typeof(RecordCommand)), entityId, point ?? "no transition", recordId);
}
