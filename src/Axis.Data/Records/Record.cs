using System.Text.Json.Nodes;

namespace Axis.Data.Records;

/// <summary>
/// A stored record. <see cref="Values"/> holds every declared field of the entity under its
/// declared name, in declaration order; a SQL <c>NULL</c> is a <see langword="null"/> value. A
/// child collection is a <see cref="JsonArray"/> of row objects in position order, and is left
/// out of a list's records.
/// <see cref="Labels"/> maps each non-null reference field name to the display field value of the
/// referenced record, in declaration order; it is empty when there is none. JSON writes it as
/// <c>labels</c> after <c>values</c>.
/// </summary>
public sealed record Record(
    Guid Id,
    long Version,
    IReadOnlyDictionary<string, JsonNode?> Values,
    IReadOnlyDictionary<string, string> Labels);

/// <summary>One page of records and the number of records in the whole table.</summary>
public sealed record RecordPage(IReadOnlyList<Record> Items, long TotalCount);
