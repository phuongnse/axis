using System.Data.Common;

namespace Axis.Data;

/// <summary>
/// Counts the work an application still has open on its releases. The processes module implements it,
/// because modules do not read each other's tables. Activation calls it to find the old-release work
/// that a destructive migration would break.
/// </summary>
public interface IOpenInstanceCounter
{
    /// <summary>
    /// The <c>running</c> and <c>waiting</c> process instances of the application, by the release they are pinned to.
    /// A release with no open instance is not in the result. The count joins <paramref name="transaction"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByReleaseAsync(DbTransaction transaction, Guid applicationId, CancellationToken cancellationToken = default);
}
