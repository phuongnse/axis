using System.Text.Json.Nodes;

namespace Axis.Data.Records;

/// <summary>
/// A stored record. <see cref="Values"/> holds every declared field of the entity under its
/// declared name, in declaration order; a SQL <c>NULL</c> is a <see langword="null"/> value.
/// </summary>
public sealed record Record(Guid Id, long Version, IReadOnlyDictionary<string, JsonValue?> Values);

/// <summary>One page of records and the number of records in the whole table.</summary>
public sealed record RecordPage(IReadOnlyList<Record> Items, long TotalCount);
