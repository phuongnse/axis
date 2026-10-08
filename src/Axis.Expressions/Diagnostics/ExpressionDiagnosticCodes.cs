namespace Axis.Expressions.Diagnostics;

/// <summary>
/// Stable diagnostic codes for expressions. They share the <c>AXCnnnn</c> range and the docs table
/// with the configuration codes. A code never changes meaning once published.
/// </summary>
public static class ExpressionDiagnosticCodes
{
    public const string SyntaxError = "AXC0035";
    public const string TooLong = "AXC0036";
    public const string TooDeep = "AXC0037";
    public const string TooManyNodes = "AXC0038";
}
