namespace Axis.Mcp.Api;

public sealed class AxisApiException(
    int statusCode,
    string message,
    string? problemCode = null,
    string? problemType = null,
    IReadOnlyDictionary<string, string[]>? fieldErrors = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? ProblemCode { get; } = problemCode;
    public string? ProblemType { get; } = problemType;
    public IReadOnlyDictionary<string, string[]> FieldErrors { get; } =
        fieldErrors ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
}
