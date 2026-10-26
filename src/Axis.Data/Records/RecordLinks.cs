using Axis.Configuration.Model;

namespace Axis.Data.Records;

/// <summary>
/// The record ids a body sends for one <c>references</c> field of <see cref="Field"/>, in the order
/// they were written. The ids are distinct, and there are at most <see cref="Max"/> of them.
/// </summary>
public sealed record RecordLinks(FieldModel Field, IReadOnlyList<Guid> Ids)
{
    /// <summary>The most ids one set may hold.</summary>
    public const int Max = 1000;
}
