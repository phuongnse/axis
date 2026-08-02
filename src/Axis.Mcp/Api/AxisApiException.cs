namespace Axis.Mcp.Api;

public sealed class AxisApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
