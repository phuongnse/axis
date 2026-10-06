namespace Axis.Data.Records;

/// <summary>
/// The outcome of parsing a record request body: either the input or the errors, never both.
/// Errors are keyed by RFC 6901 JSON Pointer into the body, in ordinal key order.
/// </summary>
public sealed class RecordInputResult
{
    private RecordInputResult(RecordInput? input, SortedDictionary<string, string[]>? errors)
    {
        Input = input;
        Errors = errors;
    }

    public RecordInput? Input { get; }

    public SortedDictionary<string, string[]>? Errors { get; }

    internal static RecordInputResult Success(RecordInput input) => new(input, null);

    internal static RecordInputResult Failure(SortedDictionary<string, string[]> errors) => new(null, errors);
}
