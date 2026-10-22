namespace Axis.Data.Records;

/// <summary>
/// A draft record's <c>values</c>, read for form state. <see cref="Input"/> holds every valid value
/// and row, and <see cref="Errors"/> the value errors, keyed by RFC 6901 JSON Pointer in ordinal key
/// order. Both can be filled, because one bad value must not hide the state of the others.
/// </summary>
public sealed record RecordDraftResult(RecordInput Input, SortedDictionary<string, string[]> Errors);
