using Microsoft.Extensions.Logging;

namespace Axis.Processes.Records;

/// <summary>
/// Log lines for record filters that fail at run time. A line names the policy, the grant's index
/// in its grants and the entity or the data source. It never holds a record value or the
/// database's message.
/// </summary>
public static partial class RecordFilterLog
{
    [LoggerMessage(LogLevel.Warning, "The filter of grant {Grant} of policy {Policy} could not be evaluated for a record of entity {Entity}, so it granted nothing for it.")]
    public static partial void FilterFailed(ILogger logger, string policy, int grant, string entity);

    [LoggerMessage(LogLevel.Warning, "A record list of entity {Entity} failed because its read filters could not be evaluated. They include the filter of grant {Grant} of policy {Policy}.")]
    public static partial void ListFailed(ILogger logger, string policy, int grant, string entity);

    [LoggerMessage(LogLevel.Warning, "A data source {DataSource} failed because its filter or the read filters could not be evaluated. The read filters include the filter of grant {Grant} of policy {Policy}.")]
    public static partial void DataSourceFailed(ILogger logger, string policy, int grant, string dataSource);
}
